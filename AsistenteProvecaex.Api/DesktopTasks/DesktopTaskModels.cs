namespace Asistente.Api.DesktopTasks;

/// <summary>
/// Prioridad visual de una tarea.
/// </summary>
public enum DesktopTaskPriority
{
    Normal,
    Urgente,
    MuyUrgente
}

/// <summary>
/// Parser seguro de prioridad.
/// </summary>
public static class DesktopTaskPriorityParser
{
    public static DesktopTaskPriority ParseOrDefault(
        string? value,
        DesktopTaskPriority defaultValue = DesktopTaskPriority.Normal)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        var normalized = value
            .Trim()
            .ToUpperInvariant()
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "");

        return normalized switch
        {
            "MUYURGENTE" => DesktopTaskPriority.MuyUrgente,
            "URGENTE" => DesktopTaskPriority.Urgente,
            "NORMAL" => DesktopTaskPriority.Normal,
            _ => defaultValue
        };
    }
}

/// <summary>
/// Identidad del Desktop que llama a la API.
/// </summary>
public sealed class DesktopTaskClientDto
{
    public string UserName { get; set; } = "";

    public string DomainName { get; set; } = "";

    public string MachineName { get; set; } = "";

    public string DisplayName { get; set; } = "";
}

/// <summary>
/// Grupo visible para el usuario actual.
/// </summary>
public sealed class DesktopTaskGroupDto
{
    public string Name { get; set; } = "";

    public string DisplayName { get; set; } = "";
}

/// <summary>
/// Petición para consultar grupos disponibles.
/// </summary>
public sealed class DesktopTaskGroupsRequest
{
    public DesktopTaskClientDto Client { get; set; } = new();
}

/// <summary>
/// Respuesta de grupos disponibles.
/// </summary>
public sealed class DesktopTaskGroupsResponse
{
    public int Count { get; set; }

    public List<DesktopTaskGroupDto> Groups { get; set; } = [];
}

/// <summary>
/// Petición para listar tareas visibles para un usuario/equipo.
/// </summary>
public sealed class DesktopTasksForClientRequest
{
    public DesktopTaskClientDto Client { get; set; } = new();
}

/// <summary>
/// Tarea o recordatorio creado desde el Desktop.
/// </summary>
public sealed class DesktopTaskDto
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string WrittenBy { get; set; } = "";

    public string AssignedTo { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime DueAt { get; set; }

    public string Description { get; set; } = "";

    /// <summary>
    /// Grupo interno al que pertenece la tarea.
    /// Valor por defecto para compatibilidad con tareas antiguas.
    /// </summary>
    public string GroupName { get; set; } = "informatica";

    /// <summary>
    /// Nombre visible del grupo.
    /// </summary>
    public string GroupDisplayName { get; set; } = "Informática";

    /// <summary>
    /// Prioridad: Normal, Urgente, MuyUrgente.
    /// </summary>
    public string Priority { get; set; } = nameof(DesktopTaskPriority.Normal);

    /// <summary>
    /// Archivos adjuntos vinculados a la tarea.
    /// </summary>
    public List<DesktopTaskAttachmentDto> Attachments { get; set; } = [];

    /// <summary>
    /// Número de archivos adjuntos.
    /// </summary>
    public int AttachmentCount => Attachments?.Count ?? 0;
}

/// <summary>
/// Petición para crear una tarea desde Desktop.
/// </summary>
public sealed class CreateDesktopTaskRequest
{
    public DesktopTaskClientDto Client { get; set; } = new();

    public string WrittenBy { get; set; } = "";

    public string AssignedTo { get; set; } = "";

    public DateTime DueAt { get; set; }

    public string Description { get; set; } = "";

    public string GroupName { get; set; } = "";

    public string Priority { get; set; } = nameof(DesktopTaskPriority.Normal);
}

/// <summary>
/// Respuesta de listado de tareas.
/// </summary>
public sealed class DesktopTasksResponse
{
    public int Count { get; set; }

    public List<DesktopTaskDto> Tasks { get; set; } = [];
}

/// <summary>
/// Petición para eliminar tareas seleccionadas.
/// </summary>
public sealed class DeleteDesktopTasksRequest
{
    public DesktopTaskClientDto Client { get; set; } = new();

    public List<Guid> TaskIds { get; set; } = [];
}

/// <summary>
/// Resultado de eliminación de tareas.
/// </summary>
public sealed class DeleteDesktopTasksResult
{
    public bool Success { get; set; }

    public int RequestedCount { get; set; }

    public int DeletedCount { get; set; }

    public string Message { get; set; } = "";
}

/// <summary>
/// Petición para aplazar la fecha límite de una tarea.
/// </summary>
public sealed class PostponeDesktopTaskRequest
{
    public DesktopTaskClientDto Client { get; set; } = new();

    public Guid TaskId { get; set; }

    public DateTime NewDueAt { get; set; }
}

/// <summary>
/// Resultado de aplazar una tarea.
/// </summary>
public sealed class PostponeDesktopTaskResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public DesktopTaskDto? Task { get; set; }
}

/// <summary>
/// Petición para actualizar la descripción de una tarea.
/// </summary>
public sealed class UpdateDesktopTaskDescriptionRequest
{
    public DesktopTaskClientDto Client { get; set; } = new();

    public Guid TaskId { get; set; }

    public string Description { get; set; } = "";
}

/// <summary>
/// Resultado de actualizar la descripción de una tarea.
/// </summary>
public sealed class UpdateDesktopTaskDescriptionResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public DesktopTaskDto? Task { get; set; }
}

/// <summary>
/// Archivo adjunto vinculado a una tarea.
/// </summary>
public sealed class DesktopTaskAttachmentDto
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FileName { get; set; } = "";

    public string StoredFileName { get; set; } = "";

    public DateTime UploadedAt { get; set; } = DateTime.Now;

    public string UploadedBy { get; set; } = "";

    public long SizeBytes { get; set; }
}

/// <summary>
/// Resultado de subir un adjunto.
/// </summary>
public sealed class UploadDesktopTaskAttachmentResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public DesktopTaskAttachmentDto? Attachment { get; set; }

    public DesktopTaskDto? Task { get; set; }
}

/// <summary>
/// Petición para descargar un adjunto.
/// </summary>
public sealed class DownloadDesktopTaskAttachmentRequest
{
    public DesktopTaskClientDto Client { get; set; } = new();

    public Guid TaskId { get; set; }

    public Guid AttachmentId { get; set; }
}

/// <summary>
/// Resultado interno de localización de adjunto.
/// </summary>
public sealed class DesktopTaskAttachmentFileResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public string FullPath { get; set; } = "";

    public string FileName { get; set; } = "";

    public long SizeBytes { get; set; }
}

/// <summary>
/// Petición para eliminar un archivo adjunto de una tarea.
/// </summary>
public sealed class DeleteDesktopTaskAttachmentRequest
{
    public DesktopTaskClientDto Client { get; set; } = new();

    public Guid TaskId { get; set; }

    public Guid AttachmentId { get; set; }
}

/// <summary>
/// Resultado de eliminar un archivo adjunto.
/// </summary>
public sealed class DeleteDesktopTaskAttachmentResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public DesktopTaskDto? Task { get; set; }
}