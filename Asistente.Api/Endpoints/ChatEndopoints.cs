using System.Net.Http.Json;
using System.Text.Json;

/// <summary>
/// Endpoints de chat heredados sin motor de herramientas.
///
/// Se mantienen como rutas de diagnóstico y compatibilidad:
/// - /api/chat devuelve una respuesta completa.
/// - /api/chat/stream devuelve respuesta progresiva.
///
/// El canal principal del avatar Desktop es:
/// POST /api/tools/chat/stream
/// </summary>
public static class ChatEndpoints
{
    /// <summary>
    /// Registra los endpoints de chat sin tool calling.
    /// </summary>
    public static WebApplication MapChatEndpoints(
        this WebApplication app)
    {
        // -------------------------------------------------------------
        // POST /api/chat
        // -------------------------------------------------------------
        // Endpoint de conversación no streaming con RAG,
        // pero sin motor de herramientas.
        // -------------------------------------------------------------
        app.MapPost("/api/chat", async (
            ChatRequest request,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            KnowledgeContextService knowledgeContextService,
            HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return Results.BadRequest(new
                {
                    error = "El mensaje no puede estar vacío."
                });
            }

            var model = string.IsNullOrWhiteSpace(request.Model)
                ? configuration["Ollama:Model"] ?? "asistente-provelab:7b"
                : request.Model;

            var baseSystemPrompt = configuration["Ollama:SystemPrompt"]
                ?? "Responde siempre en español.";

            var knowledgeContext =
                await knowledgeContextService.BuildContextAsync(
                    request.Message,
                    context.RequestAborted
                );

            var systemPrompt =
                knowledgeContextService.BuildSystemPromptWithKnowledge(
                    baseSystemPrompt,
                    knowledgeContext
                );

            var messages = new List<OllamaMessage>
            {
                new()
                {
                    Role = "system",
                    Content = systemPrompt
                }
            };

            if (request.History is not null)
            {
                foreach (var historyItem in request.History)
                {
                    messages.Add(new OllamaMessage
                    {
                        Role = historyItem.Role,
                        Content = historyItem.Content
                    });
                }
            }

            messages.Add(new OllamaMessage
            {
                Role = "user",
                Content = request.Message
            });

            var ollamaRequest = new OllamaChatRequest
            {
                Model = model,
                Messages = messages,
                Stream = false
            };

            var ollamaClient =
                httpClientFactory.CreateClient("Ollama");

            var response = await ollamaClient.PostAsJsonAsync(
                "/api/chat",
                ollamaRequest,
                context.RequestAborted
            );

            var json = await response.Content.ReadAsStringAsync(
                context.RequestAborted
            );

            if (!response.IsSuccessStatusCode)
            {
                return Results.Problem(
                    title: "Error consultando Ollama",
                    detail:
                        $"Ollama respondió con estado {(int)response.StatusCode}: {json}",
                    statusCode: 500
                );
            }

            var ollamaResponse =
                JsonSerializer.Deserialize<OllamaChatResponse>(
                    json,
                    JsonOptions.Default
                );

            var answer =
                ollamaResponse?.Message?.Content ?? "";

            return Results.Ok(new ChatResponse
            {
                Model = model,
                Answer = answer,
                KnowledgeDocuments = knowledgeContext.Results
                    .Select(result => result.RelativePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            });
        });

        // -------------------------------------------------------------
        // POST /api/chat/stream
        // -------------------------------------------------------------
        // Endpoint de conversación con RAG y respuesta streaming,
        // pero sin motor de herramientas.
        // -------------------------------------------------------------
        app.MapPost("/api/chat/stream", async (
            ChatRequest request,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            KnowledgeContextService knowledgeContextService,
            HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                context.Response.StatusCode = 400;

                await context.Response.WriteAsync(
                    "El mensaje no puede estar vacío.",
                    context.RequestAborted
                );

                return;
            }

            var model = string.IsNullOrWhiteSpace(request.Model)
                ? configuration["Ollama:Model"] ?? "asistente-provelab:7b"
                : request.Model;

            var baseSystemPrompt = configuration["Ollama:SystemPrompt"]
                ?? "Responde siempre en español.";

            var knowledgeContext =
                await knowledgeContextService.BuildContextAsync(
                    request.Message,
                    context.RequestAborted
                );

            var systemPrompt =
                knowledgeContextService.BuildSystemPromptWithKnowledge(
                    baseSystemPrompt,
                    knowledgeContext
                );

            var messages = new List<OllamaMessage>
            {
                new()
                {
                    Role = "system",
                    Content = systemPrompt
                }
            };

            if (request.History is not null)
            {
                foreach (var historyItem in request.History)
                {
                    messages.Add(new OllamaMessage
                    {
                        Role = historyItem.Role,
                        Content = historyItem.Content
                    });
                }
            }

            messages.Add(new OllamaMessage
            {
                Role = "user",
                Content = request.Message
            });

            var ollamaRequest = new OllamaChatRequest
            {
                Model = model,
                Messages = messages,
                Stream = true
            };

            var ollamaClient =
                httpClientFactory.CreateClient("Ollama");

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/chat"
            )
            {
                Content = JsonContent.Create(ollamaRequest)
            };

            using var ollamaResponse =
                await ollamaClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    context.RequestAborted
                );

            if (!ollamaResponse.IsSuccessStatusCode)
            {
                var errorText =
                    await ollamaResponse.Content.ReadAsStringAsync(
                        context.RequestAborted
                    );

                context.Response.StatusCode = 500;
                context.Response.ContentType = "text/plain; charset=utf-8";

                await context.Response.WriteAsync(
                    $"Error consultando Ollama: {errorText}",
                    context.RequestAborted
                );

                return;
            }

            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache";

            await using var responseStream =
                await ollamaResponse.Content.ReadAsStreamAsync(
                    context.RequestAborted
                );

            using var reader =
                new StreamReader(responseStream);

            while (!reader.EndOfStream &&
                   !context.RequestAborted.IsCancellationRequested)
            {
                var line =
                    await reader.ReadLineAsync(
                        context.RequestAborted
                    );

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                OllamaChatResponse? chunk;

                try
                {
                    chunk =
                        JsonSerializer.Deserialize<OllamaChatResponse>(
                            line,
                            JsonOptions.Default
                        );
                }
                catch
                {
                    continue;
                }

                var textPart =
                    chunk?.Message?.Content;

                if (!string.IsNullOrEmpty(textPart))
                {
                    await context.Response.WriteAsync(
                        textPart,
                        context.RequestAborted
                    );

                    await context.Response.Body.FlushAsync(
                        context.RequestAborted
                    );
                }
            }
        });

        return app;
    }
}
