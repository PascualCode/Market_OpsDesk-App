/// <summary>
/// Representa una entrada individual de log generada por la API.
///
/// Registra información útil para trazabilidad:
/// - Fecha y hora.
/// - IP cliente.
/// - Método y ruta.
/// - Código HTTP devuelto.
/// - Modelo usado.
/// - Longitud de la consulta.
/// - Duración de la petición.
/// - Error, si se produjo.
/// </summary>
public sealed class ApiLogEntry
{
    public DateTime Date { get; set; }

    public string ClientIp { get; set; } = "";

    public string Method { get; set; } = "";

    public string Path { get; set; } = "";

    public int StatusCode { get; set; }

    public string? Model { get; set; }

    public int? QuestionLength { get; set; }

    public long ElapsedMs { get; set; }

    public string? Error { get; set; }
}