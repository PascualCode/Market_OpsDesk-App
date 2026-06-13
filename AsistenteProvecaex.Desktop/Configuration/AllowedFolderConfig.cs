namespace Asistente.Desktop.Configuration;

/// <summary>
/// Carpeta permitida para abrirse desde el asistente.
///
/// La ruta se define de forma exacta en el config.json
/// local de cada equipo.
/// </summary>
public sealed class AllowedFolderConfig
{
    /// <summary>
    /// Nombre legible de la carpeta.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Alias que el usuario puede utilizar al pedir abrirla.
    /// </summary>
    public List<string> Aliases { get; set; } = [];

    /// <summary>
    /// Ruta exacta de la carpeta que se permite abrir.
    ///
    /// Ejemplo:
    /// \\NAS380AAC\Recurso Compartido\Usuarios\Luismi
    /// </summary>
    public string FolderPath { get; set; } = "";
}