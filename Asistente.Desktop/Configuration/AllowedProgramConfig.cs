namespace Asistente.Desktop.Configuration;

/// <summary>
/// Programa permitido para ser cerrado desde el asistente.
///
/// La decisión final de seguridad se toma en el Desktop:
/// aunque la API solicite cerrar un programa, solo se ejecutará
/// si encaja con esta lista blanca.
/// </summary>
public sealed class AllowedProgramConfig
{
    /// <summary>
    /// Nombre legible que se mostrará en respuestas y resultados.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Formas alternativas en las que el usuario puede pedirlo.
    /// Ejemplos: "edge", "navegador edge".
    /// </summary>
    public List<string> Aliases { get; set; } = [];

    /// <summary>
    /// Nombres reales de proceso en Windows, sin .exe.
    /// Ejemplos: notepad, msedge.
    /// </summary>
    public List<string> ProcessNames { get; set; } = [];
}
