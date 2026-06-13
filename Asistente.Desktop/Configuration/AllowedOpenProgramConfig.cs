namespace Asistente.Desktop.Configuration;

/// <summary>
/// Programa permitido para abrirse desde el asistente.
///
/// La ejecución final se realiza en el Desktop
/// utilizando la ruta o comando configurado.
/// </summary>
public sealed class AllowedOpenProgramConfig
{
    /// <summary>
    /// Nombre legible del programa.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Alias que el usuario puede utilizar al pedir abrirlo.
    /// </summary>
    public List<string> Aliases { get; set; } = [];

    /// <summary>
    /// Ejecutable o ruta de arranque.
    /// Ejemplos:
    /// - notepad.exe
    /// - msedge.exe
    /// - C:\Ruta\Programa.exe
    /// </summary>
    public string ExecutablePath { get; set; } = "";
}
