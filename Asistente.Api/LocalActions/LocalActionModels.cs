/// <summary>
/// Acción local pendiente o ya procesada por un Desktop.
///
/// Estas acciones se generan desde la API,
/// pero deben ejecutarse físicamente en el equipo del usuario.
/// </summary>
public sealed class LocalActionItem
{
    /// <summary>
    /// Identificador único completo.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Identificador corto útil para mostrar en logs o diagnósticos.
    /// </summary>
    public string ShortId =>
        Id.Length <= 8
            ? Id
            : Id[..8];

    /// <summary>
    /// Usuario propietario de la acción.
    /// Ejemplo: supermercado\puesto50
    /// </summary>
    public string OwnerKey { get; set; } = "";

    /// <summary>
    /// Equipo concreto que debe recoger y ejecutar la acción.
    /// Ejemplo: INFORMATICA03
    /// </summary>
    public string MachineName { get; set; } = "";

    /// <summary>
    /// Tipo de acción local.
    /// Primera versión:
    /// close_program
    /// </summary>
    public string ActionType { get; set; } = "";

    /// <summary>
    /// Programa objetivo solicitado por el usuario.
    /// Ejemplo: Microsoft Edge.
    /// </summary>
    public string Target { get; set; } = "";

    /// <summary>
    /// Estado de la acción.
    /// Valores previstos:
    /// pending, completed, failed.
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// Resultado devuelto por el Desktop tras ejecutar la acción.
    /// </summary>
    public string? ResultMessage { get; set; }

    /// <summary>
    /// Momento de creación de la acción, en UTC.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Momento en que el Desktop la procesó, en UTC.
    /// </summary>
    public DateTimeOffset? ProcessedAtUtc { get; set; }
}

/// <summary>
/// Petición enviada por el Desktop para consultar
/// acciones locales pendientes de ejecución.
/// </summary>
public sealed class PendingLocalActionsRequest
{
    public AssistantClientContext Client { get; set; } = new();
}


/// <summary>
/// Petición enviada por el Desktop para informar
/// del resultado de una acción local procesada.
/// </summary>
public sealed class CompleteLocalActionRequest
{
    public AssistantClientContext Client { get; set; } = new();

    /// <summary>
    /// Identificador completo de la acción procesada.
    /// </summary>
    public string ActionId { get; set; } = "";

    /// <summary>
    /// Indica si la acción se ha ejecutado correctamente.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Mensaje descriptivo devuelto por el Desktop.
    /// </summary>
    public string? ResultMessage { get; set; }
}