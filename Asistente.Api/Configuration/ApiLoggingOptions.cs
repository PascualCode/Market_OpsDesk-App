/// <summary>
/// Configuración del sistema de logs básicos de la API.
/// </summary>
public sealed class ApiLoggingOptions
{
    public bool Enabled { get; set; } = true;

    public string Directory { get; set; } = "logs";
}