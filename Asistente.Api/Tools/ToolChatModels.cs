/// <summary>
/// Petición enviada al motor de herramientas.
///
/// Incluye:
/// - Mensaje actual del usuario.
/// - Modelo opcional.
/// - Historial conversacional.
/// - Contexto del cliente Windows.
/// </summary>
public sealed class ToolChatRequest
{
    public string Message { get; set; } = "";

    public string? Model { get; set; }

    public List<ChatHistoryMessage> History { get; set; } = [];

    public AssistantClientContext Client { get; set; } = new();
}


/// <summary>
/// Respuesta no streaming del motor de herramientas.
/// Se usa principalmente para pruebas manuales desde PowerShell.
/// </summary>
public sealed class ToolChatResponse
{
    /// <summary>
    /// Indica si el modelo respondió directamente
    /// o si se ejecutaron herramientas.
    ///
    /// Valores habituales:
    /// - no_tool
    /// - tool_executed
    /// </summary>
    public string Mode { get; set; } = "";

    /// <summary>
    /// Respuesta final redactada por el asistente.
    /// </summary>
    public string Answer { get; set; } = "";

    /// <summary>
    /// Trazabilidad de las herramientas solicitadas
    /// y ejecutadas durante la petición.
    /// </summary>
    public List<ToolCallTrace> ToolCalls { get; set; } = [];
}


/// <summary>
/// Registro de una herramienta solicitada por el modelo
/// durante una conversación.
/// </summary>
public sealed class ToolCallTrace
{
    /// <summary>
    /// Nombre técnico de la herramienta.
    /// </summary>
    public string ToolName { get; set; } = "";

    /// <summary>
    /// Argumentos JSON enviados por el modelo.
    /// </summary>
    public string ArgumentsJson { get; set; } = "";

    /// <summary>
    /// Indica si la herramienta llegó a ejecutarse.
    /// </summary>
    public bool Executed { get; set; }

    /// <summary>
    /// Indica si la ejecución terminó correctamente.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Resultado textual devuelto por la herramienta.
    /// </summary>
    public string Result { get; set; } = "";
}