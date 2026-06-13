using System.Text.Json;
using System.Diagnostics;

/// <summary>
/// Orquestador central del motor de herramientas.
///
/// Coordina:
/// - La conversación inicial con Ollama.
/// - La detección de tool calls.
/// - La ejecución de herramientas autorizadas.
/// - La generación de la respuesta final.
/// - La recuperación de contexto RAG cuando procede.
///
/// Dispone de:
/// - Flujo no streaming para pruebas y diagnóstico.
/// - Flujo streaming para el avatar Desktop.
/// </summary>
public sealed class AssistantToolOrchestrator
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly KnowledgeContextService _knowledgeContextService;
    private readonly ToolFileLogger _toolFileLogger;
    private readonly OllamaPerformanceFileLogger _ollamaPerformanceLogger;
    private readonly ILogger<AssistantToolOrchestrator> _logger;
    private readonly IReadOnlyDictionary<string, IAssistantServerTool> _tools;

    /// <summary>
    /// Herramientas relacionadas exclusivamente con tareas.
    /// Se enviarán al modelo cuando la consulta sea claramente de tareas.
    /// </summary>
    private static readonly HashSet<string> TaskToolNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
        "create_task",
        "list_tasks",
        "complete_task",
        "delete_task",
        "delete_all_tasks"
        };

    /// <summary>
    /// Herramientas relacionadas exclusivamente con recordatorios.
    /// Se enviarán al modelo cuando la consulta sea claramente de recordatorios.
    /// </summary>
    private static readonly HashSet<string> ReminderToolNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
        "create_reminder",
        "list_reminders",
        "delete_reminder",
        "delete_all_reminders"
        };

    /// <summary>
    /// Herramientas relacionadas con el directorio telefónico interno.
    /// </summary>
    private static readonly HashSet<string> PhoneDirectoryToolNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
        "search_phone_directory"
        };

    /// <summary>
    /// Herramientas relacionadas con acciones locales
    /// que debe ejecutar el Desktop del usuario.
    /// </summary>
    private static readonly HashSet<string> LocalActionToolNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
        "request_close_local_program",
        "request_open_local_program",
        "request_open_local_folder"
        };

    public AssistantToolOrchestrator(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        KnowledgeContextService knowledgeContextService,
        ToolFileLogger toolFileLogger,
        OllamaPerformanceFileLogger ollamaPerformanceLogger,
        ILogger<AssistantToolOrchestrator> logger,
        IEnumerable<IAssistantServerTool> tools)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _knowledgeContextService = knowledgeContextService;
        _toolFileLogger = toolFileLogger;
        _ollamaPerformanceLogger = ollamaPerformanceLogger;
        _logger = logger;

        _tools = tools.ToDictionary(
            tool => tool.Name,
            tool => tool,
            StringComparer.OrdinalIgnoreCase
        );
    }

    /// <summary>
    /// Procesa una petición del motor de herramientas en modo no streaming.
    ///
    /// Flujo:
    /// 1. Construye el prompt del asistente con RAG si procede.
    /// 2. Envía la consulta a Ollama junto con las herramientas disponibles.
    /// 3. Si el modelo no solicita herramientas, devuelve la respuesta directa.
    /// 4. Si solicita herramientas, las ejecuta.
    /// 5. Devuelve los resultados al modelo para redactar la respuesta final.
    /// </summary>
    public async Task<ToolChatResponse> ProcessAsync(
        ToolChatRequest request,
        CancellationToken cancellationToken)
    {
        var model = string.IsNullOrWhiteSpace(request.Model)
            ? _configuration["Ollama:Model"] ?? "asistente-provelab:7b"
            : request.Model;

        var executionContext =
            new AssistantToolExecutionContext(request.Client);

        var baseSystemPrompt = _configuration["Ollama:SystemPrompt"]
            ?? "Responde siempre en español.";

        KnowledgePromptContext knowledgeContext;

        if (IsClearPlanningIntent(request.Message) ||
            IsClearPhoneDirectoryIntent(request.Message) ||
            IsClearCloseProgramIntent(request.Message) ||
            IsClearOpenProgramIntent(request.Message) ||
            IsClearOpenFolderIntent(request.Message))
        {
            // Para operaciones sobre planificación, directorio telefónico
            // o acciones locales del Desktop, evitamos RAG porque
            // se resuelven mediante herramientas específicas.
            knowledgeContext = new KnowledgePromptContext();
        }
        else
        {
            knowledgeContext = await _knowledgeContextService.BuildContextAsync(
                request.Message,
                cancellationToken
            );
        }

        var enrichedBaseSystemPrompt =
            _knowledgeContextService.BuildSystemPromptWithKnowledge(
                baseSystemPrompt,
                knowledgeContext
            );

        var systemPrompt = BuildToolOrchestrationPrompt(
            enrichedBaseSystemPrompt,
            request.Client
        );

        var messages = new List<OllamaMessage>
        {
            new()
            {
                Role = "system",
                Content = systemPrompt
            }
        };

        // Incorporamos el historial reciente de la conversación
        // para mantener continuidad contextual.
        foreach (var historyItem in GetOptimizedHistory(request))
        {
            messages.Add(new OllamaMessage
            {
                Role = historyItem.Role,
                Content = historyItem.Content
            });
        }

        // Añadimos el nuevo mensaje del usuario.
        messages.Add(new OllamaMessage
        {
            Role = "user",
            Content = request.Message
        });

        var toolsForModel =
            GetToolDefinitionsForRequest(request.Message);

        var firstResponse = await SendChatAsync(
            model,
            messages,
            toolsForModel,
            "tool_decision_or_direct_answer",
            cancellationToken
        );

        var assistantMessage = firstResponse.Message ?? new OllamaMessage
        {
            Role = "assistant",
            Content = ""
        };

        var toolCalls = assistantMessage.ToolCalls ?? [];

        // Si el modelo no solicita herramientas,
        // devolvemos directamente su respuesta.
        if (toolCalls.Count == 0)
        {
            return new ToolChatResponse
            {
                Mode = "no_tool",
                Answer = assistantMessage.Content
            };
        }

        var trace = new List<ToolCallTrace>();

        // Añadimos al historial el mensaje del asistente
        // que contiene las tool_calls solicitadas.
        messages.Add(new OllamaMessage
        {
            Role = "assistant",
            Content = assistantMessage.Content,
            ToolCalls = toolCalls
        });

        foreach (var toolCall in toolCalls)
        {
            var toolName = toolCall.Function.Name;

            var argumentsJson =
                toolCall.Function.Arguments.ValueKind == JsonValueKind.Undefined
                    ? "{}"
                    : toolCall.Function.Arguments.GetRawText();

            if (!_tools.TryGetValue(toolName, out var tool))
            {
                var missingToolResult = ToolExecutionResult.Fail(
                    $"La herramienta solicitada '{toolName}' no está registrada."
                );

                trace.Add(new ToolCallTrace
                {
                    ToolName = toolName,
                    ArgumentsJson = argumentsJson,
                    Executed = false,
                    Success = false,
                    Result = missingToolResult.Content
                });

                await LogToolExecutionSafeAsync(
                    executionContext,
                    toolName,
                    argumentsJson,
                    executed: false,
                    success: false,
                    elapsedMs: 0,
                    result: missingToolResult.Content
                );

                messages.Add(new OllamaMessage
                {
                    Role = "tool",
                    ToolName = toolName,
                    Content = missingToolResult.Content
                });

                continue;
            }

            ToolExecutionResult executionResult;

            var stopwatch = Stopwatch.StartNew();

            try
            {
                executionResult = await tool.ExecuteAsync(
                    toolCall.Function.Arguments,
                    executionContext,
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                executionResult = ToolExecutionResult.Fail(
                    $"La herramienta '{toolName}' produjo un error: {ex.Message}"
                );
            }
            finally
            {
                stopwatch.Stop();
            }

            trace.Add(new ToolCallTrace
            {
                ToolName = toolName,
                ArgumentsJson = argumentsJson,
                Executed = true,
                Success = executionResult.Success,
                Result = executionResult.Content
            });

            await LogToolExecutionSafeAsync(
                executionContext,
                toolName,
                argumentsJson,
                executed: true,
                success: executionResult.Success,
                elapsedMs: stopwatch.ElapsedMilliseconds,
                result: executionResult.Content
            );

            messages.Add(new OllamaMessage
            {
                Role = "tool",
                ToolName = toolName,
                Content = executionResult.Content
            });
        }

        // Segunda llamada a Ollama:
        // redacta la respuesta final con los resultados de las tools.
        var finalResponse = await SendChatAsync(
            model,
            messages,
            tools: null,
            performanceStage: "final_answer_after_tools_non_streaming",
            cancellationToken
        );

        return new ToolChatResponse
        {
            Mode = "tool_executed",
            Answer = finalResponse.Message?.Content ?? "",
            ToolCalls = trace
        };
    }

    /// <summary>
    /// Procesa una petición del motor de herramientas
    /// y devuelve la respuesta final en streaming.
    ///
    /// Este es el flujo principal usado por el avatar Desktop.
    ///
    /// Flujo:
    /// 1. Construye el prompt con RAG si procede.
    /// 2. Envía la petición a Ollama junto con las tools disponibles.
    /// 3. Si no se necesitan tools, genera respuesta final en streaming.
    /// 4. Si se necesitan tools, las ejecuta.
    /// 5. Devuelve al modelo los resultados de las tools.
    /// 6. Genera la respuesta final en streaming.
    /// </summary>
    public async Task StreamProcessAsync(
        ToolChatRequest request,
        HttpResponse httpResponse,
        CancellationToken cancellationToken)
    {
        var model = string.IsNullOrWhiteSpace(request.Model)
            ? _configuration["Ollama:Model"] ?? "asistente-provelab:7b"
            : request.Model;

        var baseSystemPrompt = _configuration["Ollama:SystemPrompt"]
            ?? "Responde siempre en español.";

        var executionContext =
            new AssistantToolExecutionContext(request.Client);

        KnowledgePromptContext knowledgeContext;

        if (IsClearPlanningIntent(request.Message) ||
            IsClearPhoneDirectoryIntent(request.Message) ||
            IsClearCloseProgramIntent(request.Message) ||
            IsClearOpenProgramIntent(request.Message) ||
            IsClearOpenFolderIntent(request.Message))
        {
            // Para operaciones sobre planificación, directorio telefónico
            // o acciones locales del Desktop, evitamos RAG porque
            // se resuelven mediante herramientas específicas.
            knowledgeContext = new KnowledgePromptContext();
        }
        else
        {
            knowledgeContext = await _knowledgeContextService.BuildContextAsync(
                request.Message,
                cancellationToken
            );
        }

        var enrichedBaseSystemPrompt =
            _knowledgeContextService.BuildSystemPromptWithKnowledge(
                baseSystemPrompt,
                knowledgeContext
            );

        var systemPrompt = BuildToolOrchestrationPrompt(
            enrichedBaseSystemPrompt,
            request.Client
        );

        var messages = new List<OllamaMessage>
        {
            new()
            {
                Role = "system",
                Content = systemPrompt
            }
        };

        // Incorporamos el historial reciente de conversación.
        foreach (var historyItem in GetOptimizedHistory(request))
        {
            messages.Add(new OllamaMessage
            {
                Role = historyItem.Role,
                Content = historyItem.Content
            });
        }

        // Añadimos el nuevo mensaje del usuario.
        messages.Add(new OllamaMessage
        {
            Role = "user",
            Content = request.Message
        });

        var toolsForModel =
            GetToolDefinitionsForRequest(request.Message);

        // Primera llamada:
        // el modelo decide si necesita herramientas.
        var firstResponse = await SendChatAsync(
            model,
            messages,
            toolsForModel,
            "tool_decision_or_direct_answer",
            cancellationToken
        );

        var assistantMessage = firstResponse.Message ?? new OllamaMessage
        {
            Role = "assistant",
            Content = ""
        };

        var toolCalls = assistantMessage.ToolCalls ?? [];

        // Caso 1:
        // El modelo NO solicita herramientas.
        //
        // En este caso NO hacemos una segunda llamada a Ollama.
        // Reutilizamos directamente la respuesta ya generada en la primera
        // llamada, porque:
        // - Evita latencia innecesaria.
        // - Evita que una segunda inferencia sin tools invente acciones
        //   que realmente no se han ejecutado.
        if (toolCalls.Count == 0)
        {
            await StreamPreparedAnswerAsync(
                assistantMessage.Content,
                httpResponse,
                cancellationToken
            );

            return;
        }

        // Caso 2:
        // El modelo solicita una o varias herramientas.
        messages.Add(new OllamaMessage
        {
            Role = "assistant",
            Content = assistantMessage.Content,
            ToolCalls = toolCalls
        });

        foreach (var toolCall in toolCalls)
        {
            var toolName = toolCall.Function.Name;

            var argumentsJson =
                toolCall.Function.Arguments.ValueKind == JsonValueKind.Undefined
                    ? "{}"
                    : toolCall.Function.Arguments.GetRawText();

            if (!_tools.TryGetValue(toolName, out var tool))
            {
                var missingToolResult =
                    $"La herramienta solicitada '{toolName}' no está registrada.";

                await LogToolExecutionSafeAsync(
                    executionContext,
                    toolName,
                    argumentsJson,
                    executed: false,
                    success: false,
                    elapsedMs: 0,
                    result: missingToolResult
                );

                messages.Add(new OllamaMessage
                {
                    Role = "tool",
                    ToolName = toolName,
                    Content = missingToolResult
                });

                continue;
            }

            ToolExecutionResult executionResult;

            var stopwatch = Stopwatch.StartNew();

            try
            {
                executionResult = await tool.ExecuteAsync(
                    toolCall.Function.Arguments,
                    executionContext,
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                executionResult = ToolExecutionResult.Fail(
                    $"La herramienta '{toolName}' produjo un error: {ex.Message}"
                );
            }
            finally
            {
                stopwatch.Stop();
            }

            await LogToolExecutionSafeAsync(
                executionContext,
                toolName,
                argumentsJson,
                executed: true,
                success: executionResult.Success,
                elapsedMs: stopwatch.ElapsedMilliseconds,
                result: executionResult.Content
            );

            messages.Add(new OllamaMessage
            {
                Role = "tool",
                ToolName = toolName,
                Content = executionResult.Content
            });
        }

        // Segunda llamada:
        // el modelo redacta la respuesta final en streaming
        // usando el resultado de las herramientas.
        await StreamFinalAnswerAsync(
            model,
            messages,
            httpResponse,
            "final_answer_after_tools_streaming",
            cancellationToken
        );
    }

    /// <summary>
    /// Envía una petición de chat no streaming a Ollama.
    ///
    /// Se utiliza para:
    /// - Detectar si el modelo quiere ejecutar herramientas.
    /// - Generar la respuesta final no streaming tras ejecutar tools.
    /// </summary>
    private async Task<OllamaChatResponse> SendChatAsync(
        string model,
        List<OllamaMessage> messages,
        List<OllamaToolDefinition>? tools,
        string performanceStage,
        CancellationToken cancellationToken)
    {
        var ollamaClient =
            _httpClientFactory.CreateClient("Ollama");

        var request = new OllamaChatRequest
        {
            Model = model,
            Messages = messages,
            Stream = false,
            KeepAlive = _configuration["Ollama:KeepAlive"],
            Tools = tools
        };

        var response = await ollamaClient.PostAsJsonAsync(
            "/api/chat",
            request,
            cancellationToken
        );

        var json = await response.Content.ReadAsStringAsync(
            cancellationToken
        );

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Error llamando a Ollama para tool calling: " +
                $"{(int)response.StatusCode} - {json}"
            );
        }

        var ollamaResponse =
            JsonSerializer.Deserialize<OllamaChatResponse>(
                json,
                JsonOptions.Default
            ) ?? new OllamaChatResponse();

        await LogOllamaPerformanceSafeAsync(
            performanceStage,
            model,
            ollamaResponse
        );

        return ollamaResponse;
    }

    /// <summary>
    /// Solicita a Ollama una respuesta final en modo streaming
    /// y reenvía únicamente el texto al cliente HTTP.
    ///
    /// Es el mecanismo que permite que el avatar muestre
    /// la respuesta progresivamente.
    /// </summary>
    private async Task StreamFinalAnswerAsync(
        string model,
        List<OllamaMessage> messages,
        HttpResponse httpResponse,
        string performanceStage,
        CancellationToken cancellationToken)
    {
        var ollamaClient =
            _httpClientFactory.CreateClient("Ollama");

        var streamRequest = new OllamaChatRequest
        {
            Model = model,
            Messages = messages,
            Stream = true,
            KeepAlive = _configuration["Ollama:KeepAlive"],
            Tools = null
        };

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/chat"
        )
        {
            Content = JsonContent.Create(streamRequest)
        };

        using var ollamaResponse =
            await ollamaClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );

        if (!ollamaResponse.IsSuccessStatusCode)
        {
            var errorText =
                await ollamaResponse.Content.ReadAsStringAsync(
                    cancellationToken
                );

            await httpResponse.WriteAsync(
                $"Error generando respuesta del asistente: {errorText}",
                cancellationToken
            );

            return;
        }

        await using var responseStream =
            await ollamaResponse.Content.ReadAsStreamAsync(
                cancellationToken
            );

        using var reader = new StreamReader(responseStream);

        OllamaChatResponse? finalMetricsChunk = null;

        while (!reader.EndOfStream &&
               !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(
                cancellationToken
            );

            if (string.IsNullOrWhiteSpace(line))
                continue;

            OllamaChatResponse? chunk;

            try
            {
                chunk = JsonSerializer.Deserialize<OllamaChatResponse>(
                    line,
                    JsonOptions.Default
                );
            }
            catch
            {
                // Si Ollama devuelve una línea mal formada,
                // se omite sin romper la respuesta completa.
                continue;
            }

            if (chunk?.Done == true)
            {
                finalMetricsChunk = chunk;
            }

            var textPart = chunk?.Message?.Content;

            if (!string.IsNullOrEmpty(textPart))
            {
                await httpResponse.WriteAsync(
                    textPart,
                    cancellationToken
                );

                await httpResponse.Body.FlushAsync(
                    cancellationToken
                );
            }
        }

        await LogOllamaPerformanceSafeAsync(
            performanceStage,
            model,
            finalMetricsChunk
        );

    }

    /// <summary>
    /// Reenvía al cliente una respuesta que ya ha sido generada
    /// previamente por Ollama.
    ///
    /// Se utiliza cuando la primera llamada no solicita herramientas.
    /// Así evitamos una segunda inferencia innecesaria y conservamos
    /// el comportamiento de respuesta progresiva en el Desktop.
    /// </summary>
    private static async Task StreamPreparedAnswerAsync(
        string? answer,
        HttpResponse httpResponse,
        CancellationToken cancellationToken)
    {
        var finalAnswer = string.IsNullOrWhiteSpace(answer)
            ? "No he podido generar una respuesta válida."
            : answer;

        const int chunkSize = 32;

        for (var index = 0; index < finalAnswer.Length; index += chunkSize)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var length = Math.Min(
                chunkSize,
                finalAnswer.Length - index
            );

            var chunk = finalAnswer.Substring(
                index,
                length
            );

            await httpResponse.WriteAsync(
                chunk,
                cancellationToken
            );

            await httpResponse.Body.FlushAsync(
                cancellationToken
            );
        }
    }

    /// <summary>
    /// Detecta si la consulta es claramente una operación
    /// sobre tareas.
    /// </summary>
    private static bool IsClearTaskIntent(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return false;

        var text = userMessage.ToLowerInvariant();

        var mentionsTask =
            text.Contains("tarea") ||
            text.Contains("tareas");

        if (!mentionsTask)
            return false;

        var taskActionDetected =
            text.Contains("añádeme") ||
            text.Contains("anademe") ||
            text.Contains("añade") ||
            text.Contains("anade") ||
            text.Contains("crea") ||
            text.Contains("crear") ||
            text.Contains("apunta") ||
            text.Contains("anota") ||
            text.Contains("ponme") ||
            text.Contains("muéstrame") ||
            text.Contains("muestrame") ||
            text.Contains("enséñame") ||
            text.Contains("enseñame") ||
            text.Contains("dime") ||
            text.Contains("lista") ||
            text.Contains("listar") ||
            text.Contains("qué tareas") ||
            text.Contains("que tareas") ||
            text.Contains("completa") ||
            text.Contains("completar") ||
            text.Contains("finaliza") ||
            text.Contains("finalizar") ||
            text.Contains("marca") ||
            text.Contains("marcar") ||
            text.Contains("borra") ||
            text.Contains("borrar") ||
            text.Contains("elimina") ||
            text.Contains("eliminar") ||
            text.Contains("quita") ||
            text.Contains("quitar");

        return taskActionDetected;
    }

    /// <summary>
    /// Detecta si la consulta es claramente una operación
    /// sobre recordatorios o avisos.
    /// </summary>
    private static bool IsClearReminderIntent(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return false;

        var text = userMessage.ToLowerInvariant();

        // Creación típica de recordatorios, incluso si no aparece
        // literalmente la palabra "recordatorio".
        if (text.Contains("recuérdame") ||
            text.Contains("recuerdame") ||
            text.Contains("avísame") ||
            text.Contains("avisame"))
        {
            return true;
        }

        var mentionsReminder =
            text.Contains("recordatorio") ||
            text.Contains("recordatorios") ||
            text.Contains("aviso") ||
            text.Contains("avisos");

        if (!mentionsReminder)
            return false;

        var reminderActionDetected =
            text.Contains("ponme") ||
            text.Contains("crea") ||
            text.Contains("crear") ||
            text.Contains("muéstrame") ||
            text.Contains("muestrame") ||
            text.Contains("enséñame") ||
            text.Contains("enseñame") ||
            text.Contains("dime") ||
            text.Contains("lista") ||
            text.Contains("listar") ||
            text.Contains("borra") ||
            text.Contains("borrar") ||
            text.Contains("elimina") ||
            text.Contains("eliminar") ||
            text.Contains("cancela") ||
            text.Contains("cancelar");

        return reminderActionDetected;
    }

    /// <summary>
    /// Detecta consultas que son claramente de planificación.
    /// </summary>
    private static bool IsClearPlanningIntent(string userMessage)
    {
        return IsClearTaskIntent(userMessage) ||
               IsClearReminderIntent(userMessage);
    }

    /// <summary>
    /// Detecta si la consulta parece claramente relacionada
    /// con el directorio telefónico interno.
    ///
    /// Ejemplos:
    /// - Dame el teléfono de Juanjo.
    /// - ¿Cuál es la extensión de Iván Acevedo?
    /// - Busca a Jesús Manzano.
    /// - Contactos de Logística.
    /// - Teléfono del Rodeo.
    /// </summary>
    private static bool IsClearPhoneDirectoryIntent(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return false;

        var text = userMessage.ToLowerInvariant();

        var explicitDirectoryTerms =
            text.Contains("teléfono") ||
            text.Contains("telefono") ||
            text.Contains("móvil") ||
            text.Contains("movil") ||
            text.Contains("extensión") ||
            text.Contains("extension") ||
            text.Contains("directorio") ||
            text.Contains("contacto") ||
            text.Contains("contactos");

        if (explicitDirectoryTerms)
            return true;

        var searchVerbs =
            text.Contains("busca a ") ||
            text.Contains("búscame") ||
            text.Contains("buscame") ||
            text.Contains("dame el número") ||
            text.Contains("dame el numero") ||
            text.Contains("qué número") ||
            text.Contains("que numero");

        return searchVerbs;
    }

    /// <summary>
    /// Detecta peticiones claras para cerrar un programa
    /// en el equipo local del usuario.
    ///
    /// Ejemplos:
    /// - Cierra el Bloc de notas.
    /// - Cierra Microsoft Edge.
    /// - Cierra el navegador Edge.
    /// </summary>
    private static bool IsClearCloseProgramIntent(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return false;

        var text = userMessage.ToLowerInvariant();

        var closeVerbDetected =
            text.Contains("cierra ") ||
            text.Contains("cerrar ") ||
            text.Contains("ciérrame ") ||
            text.Contains("cierrame ");

        if (!closeVerbDetected)
            return false;

        var programHintDetected =
            text.Contains("programa") ||
            text.Contains("aplicación") ||
            text.Contains("aplicacion") ||
            text.Contains("app") ||
            text.Contains("navegador") ||
            text.Contains("bloc de notas") ||
            text.Contains("notepad") ||
            text.Contains("edge");

        return programHintDetected;
    }

    /// <summary>
    /// Detecta peticiones claras para abrir un programa
    /// en el equipo local del usuario.
    ///
    /// Ejemplos:
    /// - Abre Excel.
    /// - Abre Microsoft Edge.
    /// - Inicia el Bloc de notas.
    /// - Ejecuta el navegador Edge.
    /// </summary>
    private static bool IsClearOpenProgramIntent(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return false;

        var text = userMessage.ToLowerInvariant();

        var openVerbDetected =
            text.Contains("abre ") ||
            text.Contains("abrir ") ||
            text.Contains("ábreme ") ||
            text.Contains("abreme ") ||
            text.Contains("inicia ") ||
            text.Contains("iniciar ") ||
            text.Contains("ejecuta ") ||
            text.Contains("ejecutar ");

        if (!openVerbDetected)
            return false;

        // Evitamos clasificar como programa una petición de carpeta.
        var folderHintDetected =
            text.Contains("carpeta") ||
            text.Contains("directorio") ||
            text.Contains("ruta compartida") ||
            text.Contains("carpeta compartida");

        if (folderHintDetected)
            return false;

        var programHintDetected =
            text.Contains("programa") ||
            text.Contains("aplicación") ||
            text.Contains("aplicacion") ||
            text.Contains("app") ||
            text.Contains("navegador") ||
            text.Contains("bloc de notas") ||
            text.Contains("notepad") ||
            text.Contains("edge") ||
            text.Contains("explorer edge") ||
            text.Contains("edge explorer") ||
            text.Contains("excel") ||
            text.Contains("word") ||
            text.Contains("chrome") ||
            text.Contains("dimoni") ||
            text.Contains("tpv");

        return programHintDetected;
    }

    /// <summary>
    /// Detecta peticiones claras para abrir una carpeta
    /// autorizada en el equipo local del usuario.
    ///
    /// Ejemplos:
    /// - Abre mi carpeta compartida.
    /// - Abre la carpeta compartida.
    /// - Ábreme la carpeta de la NAS.
    /// </summary>
    private static bool IsClearOpenFolderIntent(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return false;

        var text = userMessage.ToLowerInvariant();

        var openVerbDetected =
            text.Contains("abre ") ||
            text.Contains("abrir ") ||
            text.Contains("ábreme ") ||
            text.Contains("abreme ");

        if (!openVerbDetected)
            return false;

        var folderHintDetected =
            text.Contains("carpeta") ||
            text.Contains("directorio") ||
            text.Contains("ubicación") ||
            text.Contains("ubicacion") ||
            text.Contains("ruta") ||
            text.Contains("compartida") ||
            text.Contains("nas") ||
            text.Contains("ruta compartida") ||
            text.Contains("carpeta compartida") ||
            text.Contains("carpeta de la nas");

        return folderHintDetected;
    }

    /// <summary>
    /// Selecciona qué herramientas se enviarán a Ollama.
    ///
    /// - Tareas → tools de tareas.
    /// - Recordatorios → tools de recordatorios.
    /// - Directorio telefónico → tool del directorio.
    /// - Acciones locales → tools de abrir/cerrar programas o carpetas.
    /// - Si mezcla intenciones, envía la unión necesaria.
    /// - Si no está clara, envía todas las herramientas disponibles.
    /// </summary>
    private List<OllamaToolDefinition> GetToolDefinitionsForRequest(
        string userMessage)
    {
        var taskIntent = IsClearTaskIntent(userMessage);
        var reminderIntent = IsClearReminderIntent(userMessage);
        var phoneDirectoryIntent = IsClearPhoneDirectoryIntent(userMessage);
        var closeProgramIntent = IsClearCloseProgramIntent(userMessage);
        var openProgramIntent = IsClearOpenProgramIntent(userMessage);
        var openFolderIntent = IsClearOpenFolderIntent(userMessage);

        var localActionIntent =
            closeProgramIntent ||
            openProgramIntent ||
            openFolderIntent;

        IEnumerable<IAssistantServerTool> selectedTools =
            _tools.Values;

        if (taskIntent ||
            reminderIntent ||
            phoneDirectoryIntent ||
            localActionIntent)
        {
            selectedTools = _tools.Values
                .Where(tool =>
                    (taskIntent && TaskToolNames.Contains(tool.Name)) ||
                    (reminderIntent && ReminderToolNames.Contains(tool.Name)) ||
                    (phoneDirectoryIntent && PhoneDirectoryToolNames.Contains(tool.Name)) ||
                    (localActionIntent && LocalActionToolNames.Contains(tool.Name))
                );
        }

        var definitions = selectedTools
            .Select(tool => tool.Definition)
            .ToList();

        if (definitions.Count == 0)
        {
            return _tools.Values
                .Select(tool => tool.Definition)
                .ToList();
        }

        return definitions;
    }

    /// <summary>
    /// Devuelve el historial que conviene enviar al modelo.
    ///
    /// Estrategia:
    /// - Acciones locales del equipo: sin historial.
    ///   Son órdenes directas y el historial está perjudicando
    ///   la invocación fiable de herramientas.
    /// - Planificación y directorio: historial mínimo reciente.
    /// - Conversación general: historial completo recibido desde Desktop.
    /// </summary>
    private static IEnumerable<ChatHistoryMessage> GetOptimizedHistory(
        ToolChatRequest request)
    {
        if (IsClearCloseProgramIntent(request.Message) ||
            IsClearOpenProgramIntent(request.Message) ||
            IsClearOpenFolderIntent(request.Message))
        {
            return [];
        }

        if (IsClearPlanningIntent(request.Message) ||
            IsClearPhoneDirectoryIntent(request.Message))
        {
            return request.History.TakeLast(2);
        }

        return request.History;
    }

    /// <summary>
    /// Construye el prompt operativo del motor de herramientas.
    ///
    /// Se mantiene deliberadamente compacto para reducir:
    /// - Tokens de entrada.
    /// - Tiempo de evaluación del prompt en Ollama.
    /// </summary>
    private static string BuildToolOrchestrationPrompt(
        string enrichedBaseSystemPrompt,
        AssistantClientContext client)
    {
        return $"""
    {enrichedBaseSystemPrompt}

    HERRAMIENTAS:
    - Usa herramientas cuando el usuario pida crear, consultar, completar o borrar tareas o recordatorios.
    - No inventes resultados ni confirmes acciones de planificación si no has usado la herramienta correspondiente.
    - Si no hace falta ninguna herramienta, responde normalmente.

    CONTEXTO:
    - Fecha y hora del servidor: {DateTime.Now:dd/MM/yyyy HH:mm:ss zzz}
    - Usuario: {client.UserName}
    - Dominio: {client.DomainName}
    - Equipo: {client.MachineName}

    TAREAS:
    - create_task: crear tareas.
    - list_tasks: consultar tareas.
    - complete_task: marcar tareas completadas.
    - delete_task: borrar una tarea concreta.
    - delete_all_tasks: borrar todas las tareas solo si el usuario lo pide claramente.
    - Para "tareas de hoy", usa list_tasks con scope "today".
    - Para "tareas pendientes", usa list_tasks con scope "pending".
    - Usa dueAt solo si el usuario indica hora concreta.
    - Si indica día pero no hora, usa dueDateLocal con formato yyyy-MM-dd.
    - No uses dueAt y dueDateLocal a la vez.
    - Si no indica fecha, crea la tarea sin fecha.

    RECORDATORIOS:
    - create_reminder: crear recordatorios, citas o avisos.
    - list_reminders: consultar recordatorios.
    - delete_reminder: borrar un recordatorio concreto.
    - delete_all_reminders: borrar todos los recordatorios solo si el usuario lo pide claramente.
    - Para "recordatorios de hoy", usa list_reminders con scope "today".
    - Para "próximos recordatorios", usa list_reminders con scope "upcoming".
    - Los recordatorios siempre necesitan fecha y hora suficientes.
    - remindAt debe ir en ISO 8601 con zona horaria.
    - Si falta una hora necesaria para crear un recordatorio, pide aclaración en vez de ejecutar la herramienta.

    DIRECTORIO TELEFÓNICO:
    - Si el usuario pregunta por teléfonos, móviles, extensiones o contactos internos, usa search_phone_directory.
    - Usa query para nombres, apellidos, alias, extensiones o números.
    - Usa category si el usuario menciona departamento o zona: Repartidores, Comerciales, Ventas, Compras, Mantenimiento, Informatica, Almacen, Logistica, Mostradores o Centro.
    - Usa center si el usuario menciona Cash, Rodeo, Peru o Centro.
    - No inventes teléfonos ni extensiones: responde solo con lo devuelto por la herramienta.

    ACCIONES LOCALES DEL EQUIPO:
    - Si el usuario pide cerrar un programa o aplicación local, debes usar request_close_local_program.
    - Si el usuario pide abrir, iniciar o ejecutar un programa local, debes usar request_open_local_program.
    - Si el usuario pide abrir una carpeta, carpeta compartida, directorio, ruta o ubicación autorizada, debes usar request_open_local_folder.
    - No respondas con instrucciones manuales ni digas que el usuario puede hacerlo por sí mismo si la petición encaja con una de estas acciones locales.
    - Para programas, envía en programName el nombre del programa tal como lo exprese el usuario, por ejemplo: "Bloc de notas", "Microsoft Edge", "Edge Explorer" o "Excel".
    - Para carpetas, envía en folderName el nombre tal como lo exprese el usuario, por ejemplo: "Mi carpeta compartida", "carpeta compartida" o "carpeta de la NAS".
    - La API solo registra la orden; el Desktop decide si el programa o carpeta está autorizado y lo ejecuta.
    - No confirmes que un programa se ha abierto o cerrado realmente: solo indica que la orden se ha enviado al equipo.
    - No confirmes que una carpeta se ha abierto realmente: solo indica que la orden se ha enviado al equipo.
    - No inventes resultados de ejecución.

    FECHAS:
    - Calcula expresiones como "hoy", "mañana" o "el viernes" usando la fecha del servidor.
    """;
    }

    /// <summary>
    /// Registra una ejecución de herramienta sin permitir que
    /// un fallo de escritura en logs interrumpa el asistente.
    /// </summary>
    private async Task LogToolExecutionSafeAsync(
        AssistantToolExecutionContext executionContext,
        string toolName,
        string argumentsJson,
        bool executed,
        bool success,
        long elapsedMs,
        string result)
    {
        try
        {
            await _toolFileLogger.LogAsync(new ToolExecutionLogEntry
            {
                Date = DateTime.Now,
                OwnerKey = executionContext.OwnerKey,
                MachineName = executionContext.Client.MachineName,
                ToolName = toolName,
                ArgumentsJson = argumentsJson,
                Executed = executed,
                Success = success,
                ElapsedMs = elapsedMs,
                Result = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "No se pudo escribir el log de herramienta. Tool={ToolName}, Owner={OwnerKey}",
                toolName,
                executionContext.OwnerKey
            );
        }
    }

    /// <summary>
    /// Registra métricas de rendimiento devueltas por Ollama
    /// sin permitir que un fallo de escritura en logs rompa el asistente.
    /// </summary>
    private async Task LogOllamaPerformanceSafeAsync(
        string stage,
        string model,
        OllamaChatResponse? response)
    {
        if (response is null)
            return;

        try
        {
            await _ollamaPerformanceLogger.LogAsync(
                new OllamaPerformanceLogEntry
                {
                    Date = DateTime.Now,
                    Stage = stage,
                    Model = model,
                    TotalMs = NanosecondsToMilliseconds(
                        response.TotalDurationNanoseconds
                    ),
                    LoadMs = NanosecondsToMilliseconds(
                        response.LoadDurationNanoseconds
                    ),
                    PromptTokens = response.PromptEvalCount,
                    PromptEvalMs = NanosecondsToMilliseconds(
                        response.PromptEvalDurationNanoseconds
                    ),
                    OutputTokens = response.EvalCount,
                    EvalMs = NanosecondsToMilliseconds(
                        response.EvalDurationNanoseconds
                    )
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "No se pudo escribir el log de rendimiento de Ollama. Stage={Stage}, Model={Model}",
                stage,
                model
            );
        }
    }

    /// <summary>
    /// Convierte nanosegundos a milisegundos.
    /// </summary>
    private static long? NanosecondsToMilliseconds(long? nanoseconds)
    {
        if (!nanoseconds.HasValue)
            return null;

        return nanoseconds.Value / 1_000_000;
    }
}
