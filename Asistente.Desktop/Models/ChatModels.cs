using Asistente.Desktop.Services;

namespace Asistente.Desktop.Models;

/// <summary>
/// Representa un mensaje histórico enviado al modelo.
/// Role debe ser:
/// - user
/// - assistant
/// </summary>
public sealed class ChatHistoryMessage
{
    public string Role { get; set; } = "";

    public string Content { get; set; } = "";
}

/// <summary>
/// Datos del usuario y equipo que se envían a la API.
/// Deben coincidir con el modelo que ya usa la API.
/// </summary>
public sealed class DesktopClientContext
{
    public string UserName { get; set; } = "";

    public string DomainName { get; set; } = "";

    public string MachineName { get; set; } = "";

    public string DisplayName { get; set; } = "";
}

/// <summary>
/// Petición enviada desde el Desktop al endpoint:
/// /api/tools/chat/stream
///
/// Incluye:
/// - Mensaje del usuario.
/// - Modelo opcional.
/// - Historial reciente de conversación.
/// - Identidad del usuario Windows.
/// </summary>
public sealed class DesktopToolChatRequest
{
    public string Message { get; set; } = "";

    public string? Model { get; set; }

    public List<ChatHistoryMessage> History { get; set; } = [];

    public DesktopClientContext Client { get; set; } = new();
}

/// <summary>
/// Resultado final de una consulta streaming al asistente.
/// </summary>
public sealed class AssistantChatStreamResult
{
    /// <summary>
    /// Indica si la petición HTTP se inició correctamente.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Respuesta completa generada por el asistente.
    /// </summary>
    public string Answer { get; set; } = "";

    /// <summary>
    /// Mensaje de error legible si la API devolvió
    /// un código HTTP no satisfactorio.
    /// </summary>
    public string? ErrorMessage { get; set; }
}