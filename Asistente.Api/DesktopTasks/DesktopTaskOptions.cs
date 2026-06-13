namespace Asistente.Api.DesktopTasks;

/// <summary>
/// Configuración del almacenamiento de tareas directas del Desktop.
/// </summary>
public sealed class DesktopTaskOptions
{
    /// <summary>
    /// Ruta del fichero JSON donde se guardarán las tareas.
    /// Si se deja vacío, se usará una ruta por defecto dentro de la carpeta de la API.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Grupos internos configurados desde el servidor.
    /// El Desktop no decide los grupos: solo envía usuario/equipo.
    /// </summary>
    public List<DesktopTaskGroupConfig> Groups { get; set; } = [];

    /// <summary>
    /// Carpeta donde se guardarán los archivos adjuntos de tareas.
    /// </summary>
    public string? AttachmentsDirectory { get; set; }

    /// <summary>
    /// Tamaño máximo por archivo adjunto en bytes.
    /// No limitamos extensión, pero sí conviene limitar tamaño.
    /// </summary>
    public long MaxAttachmentBytes { get; set; } = 500 * 1024 * 1024;
}

/// <summary>
/// Grupo interno de tareas.
/// </summary>
public sealed class DesktopTaskGroupConfig
{
    /// <summary>
    /// Identificador interno. Ejemplo: informatica, oficina.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Nombre visible en Desktop. Ejemplo: Informática, Oficina.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Miembros permitidos.
    /// Puede contener:
    /// - nombre de usuario: Luis
    /// - dominio\\usuario: DOMINIO\\Luis
    /// - nombre de equipo: EQUIPO1
    /// </summary>
    public List<string> Members { get; set; } = [];
}