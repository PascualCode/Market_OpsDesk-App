namespace Asistente.Desktop.DesktopTasks;

/// <summary>
/// Configuración del módulo de tareas/recordatorios del Desktop.
/// </summary>
public sealed class DesktopTaskConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Endpoint para crear tareas.
    /// </summary>
    public string ApiDesktopTasksUrl { get; set; } =
        "http://10.0.0.210:5055/api/desktop-tasks";

    /// <summary>
    /// Endpoint para listar solo las tareas visibles para el usuario/equipo.
    /// </summary>
    public string ApiVisibleDesktopTasksUrl { get; set; } =
        "http://10.0.0.210:5055/api/desktop-tasks/visible";

    /// <summary>
    /// Endpoint para obtener los grupos a los que pertenece el usuario/equipo.
    /// </summary>
    public string ApiDesktopTaskGroupsUrl { get; set; } =
        "http://10.0.0.210:5055/api/desktop-tasks/groups";

    /// <summary>
    /// Endpoint para eliminar tareas.
    /// </summary>
    public string ApiDeleteDesktopTasksUrl { get; set; } =
        "http://10.0.0.210:5055/api/desktop-tasks/delete";

    /// <summary>
    /// Endpoint para aplazar la fecha límite de una tarea.
    /// </summary>
    public string ApiPostponeDesktopTaskUrl { get; set; } =
        "http://10.0.0.210:5055/api/desktop-tasks/postpone";

    /// <summary>
    /// Endpoint para actualizar la descripción de una tarea.
    /// </summary>
    public string ApiUpdateDesktopTaskDescriptionUrl { get; set; } =
        "http://10.0.0.210:5055/api/desktop-tasks/update-description";

    /// <summary>
    /// Endpoint para subir archivos adjuntos a una tarea.
    /// </summary>
    public string ApiUploadDesktopTaskAttachmentUrl { get; set; } =
        "http://10.0.0.210:5055/api/desktop-tasks/attachments/upload";

    /// <summary>
    /// Endpoint para descargar archivos adjuntos de una tarea.
    /// </summary>
    public string ApiDownloadDesktopTaskAttachmentUrl { get; set; } =
        "http://10.0.0.210:5055/api/desktop-tasks/attachments/download";

    /// <summary>
    /// Endpoint para eliminar archivos adjuntos de una tarea.
    /// </summary>
    public string ApiDeleteDesktopTaskAttachmentUrl { get; set; } =
        "http://10.0.0.210:5055/api/desktop-tasks/attachments/delete";
}