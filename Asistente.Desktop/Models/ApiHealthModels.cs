namespace Asistente.Desktop.Models;

/// <summary>
/// Resultado de la comprobación de disponibilidad de la API.
/// </summary>
public sealed class ApiHealthCheckResult
{
    /// <summary>
    /// Indica si la API ha respondido con un código 2xx.
    /// </summary>
    public bool IsAvailable { get; set; }

    /// <summary>
    /// Código HTTP devuelto por la API, si ha existido respuesta.
    /// Será null si hubo error de red, timeout o DNS.
    /// </summary>
    public int? StatusCode { get; set; }
}
