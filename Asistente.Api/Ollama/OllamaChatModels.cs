using System.Text.Json.Serialization;

/// <summary>
/// Petición enviada al endpoint /api/chat de Ollama.
///
/// Puede incluir:
/// - Modelo.
/// - Historial de mensajes.
/// - Streaming.
/// - Definiciones de herramientas disponibles.
/// </summary>
public sealed class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("messages")]
    public List<OllamaMessage> Messages { get; set; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    /// <summary>
    /// Tiempo que Ollama debe mantener el modelo cargado
    /// en memoria tras completar la petición.
    ///
    /// Ejemplos:
    /// - "5m"
    /// - "30m"
    /// - "-1"
    /// </summary>
    [JsonPropertyName("keep_alive")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KeepAlive { get; set; }

    /// <summary>
    /// Herramientas disponibles para que el modelo
    /// pueda decidir si debe invocarlas.
    /// </summary>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OllamaToolDefinition>? Tools { get; set; }

}


/// <summary>
/// Mensaje individual dentro de una conversación con Ollama.
/// </summary>
public sealed class OllamaMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    /// <summary>
    /// Herramientas que el modelo solicita ejecutar.
    /// Solo aparece en mensajes con role="assistant".
    /// </summary>
    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OllamaToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// Nombre de la herramienta cuyo resultado
    /// se devuelve posteriormente al modelo.
    /// Solo aparece en mensajes con role="tool".
    /// </summary>
    [JsonPropertyName("tool_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolName { get; set; }
}


/// <summary>
/// Respuesta recibida desde Ollama en modo chat.
///
/// En streaming se recibe una sucesión de objetos de este tipo.
/// El objeto final, cuando done=true, incluye métricas internas
/// de rendimiento que nos permiten medir latencia real.
/// </summary>
public sealed class OllamaChatResponse
{
    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    /// <summary>
    /// Tiempo total de la operación en nanosegundos.
    /// </summary>
    [JsonPropertyName("total_duration")]
    public long? TotalDurationNanoseconds { get; set; }

    /// <summary>
    /// Tiempo empleado en cargar el modelo en nanosegundos.
    /// Si es alto, indica arranque frío o recarga del modelo.
    /// </summary>
    [JsonPropertyName("load_duration")]
    public long? LoadDurationNanoseconds { get; set; }

    /// <summary>
    /// Número de tokens procesados en el prompt.
    /// </summary>
    [JsonPropertyName("prompt_eval_count")]
    public int? PromptEvalCount { get; set; }

    /// <summary>
    /// Tiempo de evaluación del prompt en nanosegundos.
    /// </summary>
    [JsonPropertyName("prompt_eval_duration")]
    public long? PromptEvalDurationNanoseconds { get; set; }

    /// <summary>
    /// Número de tokens generados en la respuesta.
    /// </summary>
    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; set; }

    /// <summary>
    /// Tiempo de generación de la respuesta en nanosegundos.
    /// </summary>
    [JsonPropertyName("eval_duration")]
    public long? EvalDurationNanoseconds { get; set; }
}
