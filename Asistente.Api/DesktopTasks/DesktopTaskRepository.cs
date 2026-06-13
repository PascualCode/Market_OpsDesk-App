using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Asistente.Api.DesktopTasks;

/// <summary>
/// Repositorio centralizado de tareas del Desktop.
/// Guarda las tareas en JSON y filtra por grupos internos configurados en servidor.
/// </summary>
public sealed class DesktopTaskRepository
{
    private readonly DesktopTaskOptions _options;

    private readonly SemaphoreSlim _lock =
        new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public DesktopTaskRepository(
        IOptions<DesktopTaskOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Devuelve los grupos a los que pertenece el cliente.
    /// </summary>
    public List<DesktopTaskGroupDto> GetGroupsForClient(
        DesktopTaskClientDto client)
    {
        return GetAllowedGroupsForClient(client)
            .Select(group => new DesktopTaskGroupDto
            {
                Name = group.Name,
                DisplayName = string.IsNullOrWhiteSpace(group.DisplayName)
                    ? group.Name
                    : group.DisplayName
            })
            .OrderBy(group => group.DisplayName)
            .ToList();
    }

    /// <summary>
    /// Devuelve solo las tareas visibles para el usuario/equipo.
    /// </summary>
    public async Task<List<DesktopTaskDto>> GetVisibleAsync(
        DesktopTaskClientDto client,
        CancellationToken cancellationToken)
    {
        var allowedGroupNames =
            GetAllowedGroupsForClient(client)
                .Select(group => Normalize(group.Name))
                .ToHashSet();

        if (allowedGroupNames.Count == 0)
            return [];

        await _lock.WaitAsync(cancellationToken);

        try
        {
            var tasks =
                await ReadTasksUnsafeAsync(cancellationToken);

            return tasks
                .Where(task => allowedGroupNames.Contains(Normalize(task.GroupName)))
                .OrderBy(task => GetPriorityOrder(task.Priority))
                .ThenBy(task => task.DueAt)
                .ThenBy(task => task.CreatedAt)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Orden visual de prioridad:
    /// 0 = Muy urgente
    /// 1 = Urgente
    /// 2 = Normal
    /// </summary>
    private static int GetPriorityOrder(
        string? priority)
    {
        var normalized =
            Normalize(priority)
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("_", "");

        return normalized switch
        {
            "MUYURGENTE" => 0,
            "URGENTE" => 1,
            _ => 2
        };
    }

    /// <summary>
    /// Crea una tarea validando que el cliente pertenece al grupo destino.
    /// </summary>
    public async Task<DesktopTaskDto> CreateAsync(
        CreateDesktopTaskRequest request,
        CancellationToken cancellationToken)
    {
        var allowedGroups =
            GetAllowedGroupsForClient(request.Client);

        var selectedGroup =
            allowedGroups.FirstOrDefault(group =>
                Normalize(group.Name) == Normalize(request.GroupName)
            );

        if (selectedGroup is null)
        {
            throw new InvalidOperationException(
                "El usuario/equipo no pertenece al grupo indicado."
            );
        }

        var parsedPriority =
            DesktopTaskPriorityParser.ParseOrDefault(
                request.Priority,
                DesktopTaskPriority.Normal
            );

        await _lock.WaitAsync(cancellationToken);

        try
        {
            var tasks =
                await ReadTasksUnsafeAsync(cancellationToken);

            var task = new DesktopTaskDto
            {
                Id = Guid.NewGuid(),
                WrittenBy = request.WrittenBy.Trim(),
                AssignedTo = request.AssignedTo.Trim(),
                CreatedAt = DateTime.Now,
                DueAt = request.DueAt,
                Description = request.Description.Trim(),
                GroupName = selectedGroup.Name.Trim(),
                GroupDisplayName = string.IsNullOrWhiteSpace(selectedGroup.DisplayName)
                    ? selectedGroup.Name.Trim()
                    : selectedGroup.DisplayName.Trim(),
                Priority = parsedPriority.ToString()
            };

            tasks.Add(task);

            await WriteTasksUnsafeAsync(
                tasks,
                cancellationToken
            );

            return task;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Elimina tareas indicadas, pero solo si pertenecen a grupos visibles para el cliente.
    /// </summary>
    public async Task<int> DeleteAsync(
        IReadOnlyList<Guid> taskIds,
        DesktopTaskClientDto client,
        CancellationToken cancellationToken)
    {
        var ids =
            taskIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToHashSet();

        if (ids.Count == 0)
            return 0;

        var allowedGroupNames =
            GetAllowedGroupsForClient(client)
                .Select(group => Normalize(group.Name))
                .ToHashSet();

        if (allowedGroupNames.Count == 0)
            return 0;

        await _lock.WaitAsync(cancellationToken);

        try
        {
            var tasks =
                await ReadTasksUnsafeAsync(cancellationToken);

            var deletedTaskIds =
                tasks
                    .Where(task =>
                        ids.Contains(task.Id) &&
                        allowedGroupNames.Contains(Normalize(task.GroupName))
                    )
                    .Select(task => task.Id)
                    .ToList();

            var beforeCount =
                tasks.Count;

            tasks =
                tasks
                    .Where(task =>
                        !ids.Contains(task.Id) ||
                        !allowedGroupNames.Contains(Normalize(task.GroupName))
                    )
                    .ToList();

            await WriteTasksUnsafeAsync(
                tasks,
                cancellationToken
            );

            foreach (var deletedTaskId in deletedTaskIds)
            {
                DeleteTaskAttachmentDirectory(
                    deletedTaskId
                );
            }

            return beforeCount - tasks.Count;
        }
        finally
        {
            _lock.Release();
        }
    }

    private List<DesktopTaskGroupConfig> GetAllowedGroupsForClient(
        DesktopTaskClientDto client)
    {
        var identities =
            BuildClientIdentities(client);

        return _options.Groups
            .Where(group =>
                group.Members.Any(member =>
                    identities.Contains(Normalize(member))
                )
            )
            .ToList();
    }

    private static HashSet<string> BuildClientIdentities(
        DesktopTaskClientDto client)
    {
        var identities =
            new HashSet<string>();

        AddIdentity(identities, client.UserName);
        AddIdentity(identities, client.DisplayName);
        AddIdentity(identities, client.MachineName);

        if (!string.IsNullOrWhiteSpace(client.DomainName) &&
            !string.IsNullOrWhiteSpace(client.UserName))
        {
            AddIdentity(
                identities,
                $"{client.DomainName}\\{client.UserName}"
            );
        }

        return identities;
    }

    private static void AddIdentity(
        HashSet<string> identities,
        string? value)
    {
        var normalized =
            Normalize(value);

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            identities.Add(normalized);
        }
    }

    private static string Normalize(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return value
            .Trim()
            .ToUpperInvariant();
    }

    private async Task<List<DesktopTaskDto>> ReadTasksUnsafeAsync(
        CancellationToken cancellationToken)
    {
        var filePath =
            GetFilePath();

        if (!File.Exists(filePath))
            return [];

        var json =
            await File.ReadAllTextAsync(
                filePath,
                cancellationToken
            );

        if (string.IsNullOrWhiteSpace(json))
            return [];

        return JsonSerializer.Deserialize<List<DesktopTaskDto>>(
            json,
            JsonOptions
        ) ?? [];
    }

    private async Task WriteTasksUnsafeAsync(
        List<DesktopTaskDto> tasks,
        CancellationToken cancellationToken)
    {
        var filePath =
            GetFilePath();

        var directory =
            Path.GetDirectoryName(filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json =
            JsonSerializer.Serialize(
                tasks,
                JsonOptions
            );

        await File.WriteAllTextAsync(
            filePath,
            json,
            cancellationToken
        );
    }

    private string GetFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.FilePath))
            return _options.FilePath;

        return Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "desktop-tasks.json"
        );
    }

    /// <summary>
    /// Aplaza la fecha límite de una tarea.
    /// Solo permite modificar tareas pertenecientes a grupos visibles
    /// para el cliente indicado.
    /// </summary>
    public async Task<PostponeDesktopTaskResult> PostponeAsync(
        PostponeDesktopTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TaskId == Guid.Empty)
        {
            return new PostponeDesktopTaskResult
            {
                Success = false,
                Message = "No se ha indicado una tarea válida."
            };
        }

        if (request.NewDueAt.Date <= DateTime.Today)
        {
            return new PostponeDesktopTaskResult
            {
                Success = false,
                Message = "La nueva fecha límite debe ser posterior a hoy."
            };
        }

        var allowedGroupNames =
            GetAllowedGroupsForClient(request.Client)
                .Select(group => Normalize(group.Name))
                .ToHashSet();

        if (allowedGroupNames.Count == 0)
        {
            return new PostponeDesktopTaskResult
            {
                Success = false,
                Message = "El usuario/equipo no pertenece a ningún grupo permitido."
            };
        }

        await _lock.WaitAsync(cancellationToken);

        try
        {
            var tasks =
                await ReadTasksUnsafeAsync(cancellationToken);

            var task =
                tasks.FirstOrDefault(item =>
                    item.Id == request.TaskId
                );

            if (task is null)
            {
                return new PostponeDesktopTaskResult
                {
                    Success = false,
                    Message = "No se ha encontrado la tarea indicada."
                };
            }

            if (!allowedGroupNames.Contains(Normalize(task.GroupName)))
            {
                return new PostponeDesktopTaskResult
                {
                    Success = false,
                    Message = "No tienes permiso para modificar esta tarea."
                };
            }

            if (request.NewDueAt.Date <= task.DueAt.Date)
            {
                return new PostponeDesktopTaskResult
                {
                    Success = false,
                    Message = "La nueva fecha debe ser posterior a la fecha límite actual."
                };
            }

            task.DueAt =
                request.NewDueAt;

            await WriteTasksUnsafeAsync(
                tasks,
                cancellationToken
            );

            return new PostponeDesktopTaskResult
            {
                Success = true,
                Message = "Fecha límite aplazada correctamente.",
                Task = task
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Actualiza la descripción de una tarea.
    /// Solo permite modificar tareas pertenecientes a grupos visibles
    /// para el cliente indicado.
    /// </summary>
    public async Task<UpdateDesktopTaskDescriptionResult> UpdateDescriptionAsync(
        UpdateDesktopTaskDescriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TaskId == Guid.Empty)
        {
            return new UpdateDesktopTaskDescriptionResult
            {
                Success = false,
                Message = "No se ha indicado una tarea válida."
            };
        }

        var newDescription =
            request.Description?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(newDescription))
        {
            return new UpdateDesktopTaskDescriptionResult
            {
                Success = false,
                Message = "La descripción no puede estar vacía."
            };
        }

        var allowedGroupNames =
            GetAllowedGroupsForClient(request.Client)
                .Select(group => Normalize(group.Name))
                .ToHashSet();

        if (allowedGroupNames.Count == 0)
        {
            return new UpdateDesktopTaskDescriptionResult
            {
                Success = false,
                Message = "El usuario/equipo no pertenece a ningún grupo permitido."
            };
        }

        await _lock.WaitAsync(cancellationToken);

        try
        {
            var tasks =
                await ReadTasksUnsafeAsync(cancellationToken);

            var task =
                tasks.FirstOrDefault(item =>
                    item.Id == request.TaskId
                );

            if (task is null)
            {
                return new UpdateDesktopTaskDescriptionResult
                {
                    Success = false,
                    Message = "No se ha encontrado la tarea indicada."
                };
            }

            if (!allowedGroupNames.Contains(Normalize(task.GroupName)))
            {
                return new UpdateDesktopTaskDescriptionResult
                {
                    Success = false,
                    Message = "No tienes permiso para modificar esta tarea."
                };
            }

            task.Description =
                newDescription;

            await WriteTasksUnsafeAsync(
                tasks,
                cancellationToken
            );

            return new UpdateDesktopTaskDescriptionResult
            {
                Success = true,
                Message = "Descripción actualizada correctamente.",
                Task = task
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Guarda un archivo adjunto en el servidor y lo vincula a una tarea.
    /// Solo permite adjuntar archivos a tareas visibles para el cliente.
    /// </summary>
    public async Task<UploadDesktopTaskAttachmentResult> UploadAttachmentAsync(
        DesktopTaskClientDto client,
        Guid taskId,
        string originalFileName,
        Stream fileStream,
        long fileSizeBytes,
        CancellationToken cancellationToken)
    {
        if (taskId == Guid.Empty)
        {
            return new UploadDesktopTaskAttachmentResult
            {
                Success = false,
                Message = "No se ha indicado una tarea válida."
            };
        }

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            return new UploadDesktopTaskAttachmentResult
            {
                Success = false,
                Message = "El archivo no tiene nombre válido."
            };
        }

        if (fileSizeBytes <= 0)
        {
            return new UploadDesktopTaskAttachmentResult
            {
                Success = false,
                Message = "El archivo está vacío."
            };
        }

        if (_options.MaxAttachmentBytes > 0 &&
            fileSizeBytes > _options.MaxAttachmentBytes)
        {
            return new UploadDesktopTaskAttachmentResult
            {
                Success = false,
                Message = $"El archivo supera el tamaño máximo permitido de {_options.MaxAttachmentBytes / 1024 / 1024} MB."
            };
        }

        var allowedGroupNames =
            GetAllowedGroupsForClient(client)
                .Select(group => Normalize(group.Name))
                .ToHashSet();

        if (allowedGroupNames.Count == 0)
        {
            return new UploadDesktopTaskAttachmentResult
            {
                Success = false,
                Message = "El usuario/equipo no pertenece a ningún grupo permitido."
            };
        }

        await _lock.WaitAsync(cancellationToken);

        try
        {
            var tasks =
                await ReadTasksUnsafeAsync(cancellationToken);

            var task =
                tasks.FirstOrDefault(item =>
                    item.Id == taskId
                );

            if (task is null)
            {
                return new UploadDesktopTaskAttachmentResult
                {
                    Success = false,
                    Message = "No se ha encontrado la tarea indicada."
                };
            }

            if (!allowedGroupNames.Contains(Normalize(task.GroupName)))
            {
                return new UploadDesktopTaskAttachmentResult
                {
                    Success = false,
                    Message = "No tienes permiso para modificar esta tarea."
                };
            }

            task.Attachments ??= [];

            var attachmentId =
                Guid.NewGuid();

            var safeOriginalFileName =
                SanitizeFileName(
                    Path.GetFileName(originalFileName)
                );

            var extension =
                Path.GetExtension(safeOriginalFileName);

            var storedFileName =
                $"{attachmentId:N}{extension}";

            var taskFolder =
                Path.Combine(
                    GetAttachmentsDirectory(),
                    task.Id.ToString("N")
                );

            Directory.CreateDirectory(taskFolder);

            var fullPath =
                Path.Combine(
                    taskFolder,
                    storedFileName
                );

            await using (var outputStream = File.Create(fullPath))
            {
                await fileStream.CopyToAsync(
                    outputStream,
                    cancellationToken
                );
            }

            var attachment =
                new DesktopTaskAttachmentDto
                {
                    Id = attachmentId,
                    FileName = safeOriginalFileName,
                    StoredFileName = storedFileName,
                    UploadedAt = DateTime.Now,
                    UploadedBy = BuildUploadedByText(client),
                    SizeBytes = fileSizeBytes
                };

            task.Attachments.Add(attachment);

            await WriteTasksUnsafeAsync(
                tasks,
                cancellationToken
            );

            return new UploadDesktopTaskAttachmentResult
            {
                Success = true,
                Message = "Archivo adjuntado correctamente.",
                Attachment = attachment,
                Task = task
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Devuelve la ruta física de un adjunto si el cliente tiene permiso
    /// para ver la tarea.
    public async Task<DesktopTaskAttachmentFileResult> GetAttachmentFileAsync(
        DownloadDesktopTaskAttachmentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TaskId == Guid.Empty ||
            request.AttachmentId == Guid.Empty)
        {
            return new DesktopTaskAttachmentFileResult
            {
                Success = false,
                Message = "La tarea o el adjunto no son válidos."
            };
        }

        var allowedGroupNames =
            GetAllowedGroupsForClient(request.Client)
                .Select(group => Normalize(group.Name))
                .ToHashSet();

        if (allowedGroupNames.Count == 0)
        {
            return new DesktopTaskAttachmentFileResult
            {
                Success = false,
                Message = "El usuario/equipo no pertenece a ningún grupo permitido."
            };
        }

        await _lock.WaitAsync(cancellationToken);

        try
        {
            var tasks =
                await ReadTasksUnsafeAsync(cancellationToken);

            var task =
                tasks.FirstOrDefault(item =>
                    item.Id == request.TaskId
                );

            if (task is null)
            {
                return new DesktopTaskAttachmentFileResult
                {
                    Success = false,
                    Message = "No se ha encontrado la tarea indicada."
                };
            }

            if (!allowedGroupNames.Contains(Normalize(task.GroupName)))
            {
                return new DesktopTaskAttachmentFileResult
                {
                    Success = false,
                    Message = "No tienes permiso para descargar adjuntos de esta tarea."
                };
            }

            var attachment =
                task.Attachments?.FirstOrDefault(item =>
                    item.Id == request.AttachmentId
                );

            if (attachment is null)
            {
                return new DesktopTaskAttachmentFileResult
                {
                    Success = false,
                    Message = "No se ha encontrado el adjunto indicado."
                };
            }

            var fullPath =
                Path.Combine(
                    GetAttachmentsDirectory(),
                    task.Id.ToString("N"),
                    attachment.StoredFileName
                );

            if (!File.Exists(fullPath))
            {
                return new DesktopTaskAttachmentFileResult
                {
                    Success = false,
                    Message = "El archivo adjunto no existe en el servidor."
                };
            }

            return new DesktopTaskAttachmentFileResult
            {
                Success = true,
                Message = "Archivo localizado.",
                FullPath = fullPath,
                FileName = attachment.FileName,
                SizeBytes = attachment.SizeBytes
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    private string GetAttachmentsDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_options.AttachmentsDirectory))
            return _options.AttachmentsDirectory;

        return Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "desktop-task-files"
        );
    }

    private static string SanitizeFileName(
        string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "archivo";

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName =
                fileName.Replace(
                    invalidChar,
                    '_'
                );
        }

        return fileName.Trim();
    }

    private static string BuildUploadedByText(
        DesktopTaskClientDto client)
    {
        if (!string.IsNullOrWhiteSpace(client.DomainName) &&
            !string.IsNullOrWhiteSpace(client.UserName))
        {
            return $"{client.DomainName}\\{client.UserName}";
        }

        if (!string.IsNullOrWhiteSpace(client.UserName))
            return client.UserName;

        if (!string.IsNullOrWhiteSpace(client.MachineName))
            return client.MachineName;

        return "Desconocido";
    }

    private void DeleteTaskAttachmentDirectory(
    Guid taskId)
    {
        try
        {
            var directory =
                Path.Combine(
                    GetAttachmentsDirectory(),
                    taskId.ToString("N")
                );

            if (Directory.Exists(directory))
            {
                Directory.Delete(
                    directory,
                    recursive: true
                );
            }
        }
        catch
        {
            // No bloqueamos la eliminación de la tarea si falla la limpieza del archivo.
        }
    }

    /// <summary>
    /// Elimina un archivo adjunto de una tarea.
    /// Solo permite eliminar adjuntos de tareas visibles para el cliente.
    /// </summary>
    public async Task<DeleteDesktopTaskAttachmentResult> DeleteAttachmentAsync(
        DeleteDesktopTaskAttachmentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TaskId == Guid.Empty ||
            request.AttachmentId == Guid.Empty)
        {
            return new DeleteDesktopTaskAttachmentResult
            {
                Success = false,
                Message = "La tarea o el adjunto no son válidos."
            };
        }

        var allowedGroupNames =
            GetAllowedGroupsForClient(request.Client)
                .Select(group => Normalize(group.Name))
                .ToHashSet();

        if (allowedGroupNames.Count == 0)
        {
            return new DeleteDesktopTaskAttachmentResult
            {
                Success = false,
                Message = "El usuario/equipo no pertenece a ningún grupo permitido."
            };
        }

        string? fileToDelete = null;

        await _lock.WaitAsync(cancellationToken);

        try
        {
            var tasks =
                await ReadTasksUnsafeAsync(cancellationToken);

            var task =
                tasks.FirstOrDefault(item =>
                    item.Id == request.TaskId
                );

            if (task is null)
            {
                return new DeleteDesktopTaskAttachmentResult
                {
                    Success = false,
                    Message = "No se ha encontrado la tarea indicada."
                };
            }

            if (!allowedGroupNames.Contains(Normalize(task.GroupName)))
            {
                return new DeleteDesktopTaskAttachmentResult
                {
                    Success = false,
                    Message = "No tienes permiso para modificar esta tarea."
                };
            }

            task.Attachments ??= [];

            var attachment =
                task.Attachments.FirstOrDefault(item =>
                    item.Id == request.AttachmentId
                );

            if (attachment is null)
            {
                return new DeleteDesktopTaskAttachmentResult
                {
                    Success = false,
                    Message = "No se ha encontrado el adjunto indicado."
                };
            }

            fileToDelete =
                Path.Combine(
                    GetAttachmentsDirectory(),
                    task.Id.ToString("N"),
                    attachment.StoredFileName
                );

            task.Attachments.RemoveAll(item =>
                item.Id == request.AttachmentId
            );

            await WriteTasksUnsafeAsync(
                tasks,
                cancellationToken
            );

            if (!string.IsNullOrWhiteSpace(fileToDelete) &&
                File.Exists(fileToDelete))
            {
                File.Delete(fileToDelete);
            }

            return new DeleteDesktopTaskAttachmentResult
            {
                Success = true,
                Message = "Archivo adjunto eliminado correctamente.",
                Task = task
            };
        }
        finally
        {
            _lock.Release();
        }
    }
}