namespace Asistente.Desktop.DesktopTasks;

public sealed class DesktopTaskClientDto
{
    public string UserName { get; set; } = "";

    public string DomainName { get; set; } = "";

    public string MachineName { get; set; } = "";

    public string DisplayName { get; set; } = "";
}

public sealed class DesktopTaskGroupItem
{
    public string Name { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string DisplayText =>
        string.IsNullOrWhiteSpace(DisplayName)
            ? Name
            : DisplayName;
}

public sealed class DesktopTaskGroupsRequest
{
    public DesktopTaskClientDto Client { get; set; } = new();
}

public sealed class DesktopTaskGroupsResponse
{
    public int Count { get; set; }

    public List<DesktopTaskGroupItem> Groups { get; set; } = [];
}

public sealed class DesktopTasksForClientRequest
{
    public DesktopTaskClientDto Client { get; set; } = new();
}

public sealed class DesktopTaskItem
{
    public Guid Id { get; set; }

    public string WrittenBy { get; set; } = "";

    public string AssignedTo { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public DateTime DueAt { get; set; }

    public string Description { get; set; } = "";

    public string GroupName { get; set; } = "";

    public string GroupDisplayName { get; set; } = "";

    public string Priority { get; set; } = "Normal";

    public bool IsExpired =>
        DueAt < DateTime.Now;

    public string StatusText =>
        IsExpired ? "Fuera de plazo" : "En plazo";

    public string PriorityDisplayText =>
        Priority switch
        {
            "MuyUrgente" => "Muy urgente",
            "Urgente" => "Urgente",
            _ => "Normal"
        };

    public string DisplayText =>
        $"{DueAt:dd/MM/yyyy HH:mm} - {GroupDisplayName} - {AssignedTo} - {Description}";

    public List<DesktopTaskAttachmentItem> Attachments { get; set; } = [];

    public int AttachmentCount { get; set; }

    public string AttachmentCountText =>
        AttachmentCount == 0
            ? "Archivos: 0"
            : $"Archivos: {AttachmentCount}";
}

public sealed class CreateDesktopTaskRequest
{
    public DesktopTaskClientDto Client { get; set; } = new();

    public string WrittenBy { get; set; } = "";

    public string AssignedTo { get; set; } = "";

    public DateTime DueAt { get; set; }

    public string Description { get; set; } = "";

    public string GroupName { get; set; } = "";

    public string Priority { get; set; } = "Normal";
}

public sealed class DesktopTasksResponse
{
    public int Count { get; set; }

    public List<DesktopTaskItem> Tasks { get; set; } = [];
}

public sealed class DeleteDesktopTasksRequest
{
    public DesktopTaskClientDto Client { get; set; } = new();

    public List<Guid> TaskIds { get; set; } = [];
}

public sealed class DeleteDesktopTasksResult
{
    public bool Success { get; set; }

    public int RequestedCount { get; set; }

    public int DeletedCount { get; set; }

    public string Message { get; set; } = "";
}

public sealed class PostponeDesktopTaskRequest
{
    public DesktopTaskClientDto Client { get; set; } = new();

    public Guid TaskId { get; set; }

    public DateTime NewDueAt { get; set; }
}

public sealed class PostponeDesktopTaskResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public DesktopTaskItem? Task { get; set; }
}

public sealed class UpdateDesktopTaskDescriptionRequest
{
    public DesktopTaskClientDto Client { get; set; } = new();

    public Guid TaskId { get; set; }

    public string Description { get; set; } = "";
}

public sealed class UpdateDesktopTaskDescriptionResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public DesktopTaskItem? Task { get; set; }
}

public sealed class DesktopTaskAttachmentItem
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = "";

    public string StoredFileName { get; set; } = "";

    public DateTime UploadedAt { get; set; }

    public string UploadedBy { get; set; } = "";

    public long SizeBytes { get; set; }

    public string DisplayText =>
        $"{FileName} ({FormatSize(SizeBytes)})";

    private static string FormatSize(
        long bytes)
    {
        if (bytes >= 1024 * 1024)
            return $"{bytes / 1024d / 1024d:0.##} MB";

        if (bytes >= 1024)
            return $"{bytes / 1024d:0.##} KB";

        return $"{bytes} B";
    }
}

public sealed class UploadDesktopTaskAttachmentResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public DesktopTaskAttachmentItem? Attachment { get; set; }

    public DesktopTaskItem? Task { get; set; }
}

public sealed class DownloadDesktopTaskAttachmentRequest
{
    public DesktopTaskClientDto Client { get; set; } = new();

    public Guid TaskId { get; set; }

    public Guid AttachmentId { get; set; }
}

public sealed class DownloadDesktopTaskAttachmentResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public string FileName { get; set; } = "";

    public byte[] FileBytes { get; set; } = [];
}

public sealed class DeleteDesktopTaskAttachmentRequest
{
    public DesktopTaskClientDto Client { get; set; } = new();

    public Guid TaskId { get; set; }

    public Guid AttachmentId { get; set; }
}

public sealed class DeleteDesktopTaskAttachmentResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public DesktopTaskItem? Task { get; set; }
}