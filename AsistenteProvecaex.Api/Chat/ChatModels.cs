/// <summary>
/// Mensaje del historial conversacional que se envía a la API
/// para mantener continuidad entre preguntas y respuestas.
/// </summary>
public sealed class ChatHistoryMessage
{
    /// <summary>
    /// Rol del mensaje.
    /// Valores previstos:
    /// - user
    /// - assistant
    /// </summary>
    public string Role { get; set; } = "";

    /// <summary>
    /// Texto del mensaje histórico.
    /// </summary>
    public string Content { get; set; } = "";
}


/// <summary>
/// Petición estándar de conversación enviada a los endpoints
/// de chat sin motor de herramientas.
/// </summary>
public sealed class ChatRequest
{
    /// <summary>
    /// Mensaje actual escrito por el usuario.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Modelo opcional.
    /// Si viene vacío, se usa el configurado en appsettings.json.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Historial opcional para mantener contexto conversacional.
    /// </summary>
    public List<ChatHistoryMessage>? History { get; set; }
}


/// <summary>
/// Respuesta completa del endpoint de chat no streaming.
/// </summary>
public sealed class ChatResponse
{
    public string Model { get; set; } = "";

    public string Answer { get; set; } = "";

    /// <summary>
    /// Documentos internos que se han usado como contexto
    /// para generar la respuesta.
    /// Útil para pruebas y trazabilidad.
    /// </summary>
    public List<string> KnowledgeDocuments { get; set; } = [];
}