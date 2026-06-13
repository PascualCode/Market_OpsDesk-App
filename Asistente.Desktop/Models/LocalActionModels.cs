namespace Asistente.Desktop.Models;

/// <summary>
/// Respuesta de la API al consultar acciones locales pendientes.
/// </summary>
public sealed class PendingLocalActionsResponse
{
    public int Count { get; set; }

    public List<PendingLocalActionItem> Actions { get; set; } = [];
}

/// <summary>
/// Acción local pendiente recibida desde la API.
/// </summary>
public sealed class PendingLocalActionItem
{
    public string Id { get; set; } = "";

    public string ShortId { get; set; } = "";

    public string ActionType { get; set; } = "";

    public string Target { get; set; } = "";

    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>
/// Resultado interno de una acción local ejecutada en el Desktop.
/// </summary>
public sealed class LocalActionExecutionResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";
}

/// <summary>
/// Petición enviada por el Desktop para consultar
/// acciones locales pendientes.
/// </summary>
public sealed class PendingLocalActionsDesktopRequest
{
    public DesktopClientContext Client { get; set; } = new();
}

/// <summary>
/// Petición enviada por el Desktop para informar
/// del resultado de una acción local ya procesada.
/// </summary>
public sealed class CompleteLocalActionDesktopRequest
{
    public DesktopClientContext Client { get; set; } = new();

    public string ActionId { get; set; } = "";

    public bool Success { get; set; }

    public string? ResultMessage { get; set; }
}
