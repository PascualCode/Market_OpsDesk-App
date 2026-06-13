namespace Asistente.Api.Configuration;

/// <summary>
/// Configuración de conexión a la base de datos
/// desde la que se obtendrán los datos de productos
/// para generar carteles y etiquetas.
/// </summary>
public sealed class PosterDatabaseOptions
{
    public string ConnectionString { get; set; } = "";

    public int CommandTimeoutSeconds { get; set; } = 15;
}