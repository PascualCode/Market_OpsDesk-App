using Asistente.Desktop.Services;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.IO;

namespace Asistente.Desktop.DesktopTasks;

/// <summary>
/// Servicio específico para gestionar tareas/recordatorios del Desktop.
/// No depende del módulo de carteles ni etiquetas.
/// </summary>
public sealed class DesktopTaskService
{
    private readonly HttpClient _httpClient;
    private readonly DesktopTaskConfig _config;
    private readonly DesktopClientContextService _clientContextService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DesktopTaskService(
        DesktopTaskConfig config,
        DesktopClientContextService clientContextService)
    {
        _config = config;
        _clientContextService = clientContextService;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public async Task<List<DesktopTaskGroupItem>> GetGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
            return [];

        var request = new DesktopTaskGroupsRequest
        {
            Client = BuildClient()
        };

        var json =
            JsonSerializer.Serialize(request);

        using var content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

        using var response =
            await _httpClient.PostAsync(
                _config.ApiDesktopTaskGroupsUrl,
                content,
                cancellationToken
            );

        if (!response.IsSuccessStatusCode)
            return [];

        var responseJson =
            await response.Content.ReadAsStringAsync(
                cancellationToken
            );

        var result =
            JsonSerializer.Deserialize<DesktopTaskGroupsResponse>(
                responseJson,
                JsonOptions
            );

        return result?.Groups ?? [];
    }

    public async Task<List<DesktopTaskItem>> GetTasksAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
            return [];

        var request = new DesktopTasksForClientRequest
        {
            Client = BuildClient()
        };

        var json =
            JsonSerializer.Serialize(request);

        using var content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

        using var response =
            await _httpClient.PostAsync(
                _config.ApiVisibleDesktopTasksUrl,
                content,
                cancellationToken
            );

        if (!response.IsSuccessStatusCode)
            return [];

        var responseJson =
            await response.Content.ReadAsStringAsync(
                cancellationToken
            );

        var result =
            JsonSerializer.Deserialize<DesktopTasksResponse>(
                responseJson,
                JsonOptions
            );

        return result?.Tasks ?? [];
    }

    public async Task<DesktopTaskItem?> CreateTaskAsync(
        CreateDesktopTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
            return null;

        request.Client =
            BuildClient();

        var json =
            JsonSerializer.Serialize(request);

        using var content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

        using var response =
            await _httpClient.PostAsync(
                _config.ApiDesktopTasksUrl,
                content,
                cancellationToken
            );

        if (!response.IsSuccessStatusCode)
            return null;

        var responseJson =
            await response.Content.ReadAsStringAsync(
                cancellationToken
            );

        return JsonSerializer.Deserialize<DesktopTaskItem>(
            responseJson,
            JsonOptions
        );
    }

    public async Task<DeleteDesktopTasksResult> DeleteTasksAsync(
        IReadOnlyList<DesktopTaskItem> tasks,
        CancellationToken cancellationToken = default)
    {
        var taskIds =
            tasks
                .Where(task => task.Id != Guid.Empty)
                .Select(task => task.Id)
                .Distinct()
                .ToList();

        if (taskIds.Count == 0)
        {
            return new DeleteDesktopTasksResult
            {
                Success = false,
                Message = "No hay tareas válidas para eliminar."
            };
        }

        var request =
            new DeleteDesktopTasksRequest
            {
                Client = BuildClient(),
                TaskIds = taskIds
            };

        var json =
            JsonSerializer.Serialize(request);

        using var content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

        using var response =
            await _httpClient.PostAsync(
                _config.ApiDeleteDesktopTasksUrl,
                content,
                cancellationToken
            );

        var responseJson =
            await response.Content.ReadAsStringAsync(
                cancellationToken
            );

        if (!response.IsSuccessStatusCode)
        {
            return new DeleteDesktopTasksResult
            {
                Success = false,
                RequestedCount = taskIds.Count,
                DeletedCount = 0,
                Message = $"Error eliminando tareas. HTTP {(int)response.StatusCode}. {responseJson}"
            };
        }

        return JsonSerializer.Deserialize<DeleteDesktopTasksResult>(
            responseJson,
            JsonOptions
        ) ?? new DeleteDesktopTasksResult
        {
            Success = false,
            Message = "La API no devolvió una respuesta válida."
        };
    }

    private DesktopTaskClientDto BuildClient()
    {
        var context =
            _clientContextService.Build();

        return new DesktopTaskClientDto
        {
            UserName = context.UserName,
            DomainName = context.DomainName,
            MachineName = context.MachineName,
            DisplayName = context.DisplayName
        };
    }

    public async Task<PostponeDesktopTaskResult> PostponeTaskAsync(
    DesktopTaskItem task,
    DateTime newDueAt,
    CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            return new PostponeDesktopTaskResult
            {
                Success = false,
                Message = "El módulo de tareas está deshabilitado."
            };
        }

        var request =
            new PostponeDesktopTaskRequest
            {
                Client = BuildClient(),
                TaskId = task.Id,
                NewDueAt = newDueAt
            };

        var json =
            JsonSerializer.Serialize(request);

        using var content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

        using var response =
            await _httpClient.PostAsync(
                _config.ApiPostponeDesktopTaskUrl,
                content,
                cancellationToken
            );

        var responseJson =
            await response.Content.ReadAsStringAsync(
                cancellationToken
            );

        var result =
            JsonSerializer.Deserialize<PostponeDesktopTaskResult>(
                responseJson,
                JsonOptions
            );

        if (!response.IsSuccessStatusCode)
        {
            return result ?? new PostponeDesktopTaskResult
            {
                Success = false,
                Message = $"Error aplazando tarea. HTTP {(int)response.StatusCode}. {responseJson}"
            };
        }

        return result ?? new PostponeDesktopTaskResult
        {
            Success = false,
            Message = "La API no devolvió una respuesta válida."
        };
    }

    public async Task<UpdateDesktopTaskDescriptionResult> UpdateTaskDescriptionAsync(
    DesktopTaskItem task,
    string description,
    CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            return new UpdateDesktopTaskDescriptionResult
            {
                Success = false,
                Message = "El módulo de tareas está deshabilitado."
            };
        }

        var request =
            new UpdateDesktopTaskDescriptionRequest
            {
                Client = BuildClient(),
                TaskId = task.Id,
                Description = description
            };

        var json =
            JsonSerializer.Serialize(request);

        using var content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

        using var response =
            await _httpClient.PostAsync(
                _config.ApiUpdateDesktopTaskDescriptionUrl,
                content,
                cancellationToken
            );

        var responseJson =
            await response.Content.ReadAsStringAsync(
                cancellationToken
            );

        var result =
            JsonSerializer.Deserialize<UpdateDesktopTaskDescriptionResult>(
                responseJson,
                JsonOptions
            );

        if (!response.IsSuccessStatusCode)
        {
            return result ?? new UpdateDesktopTaskDescriptionResult
            {
                Success = false,
                Message = $"Error actualizando descripción. HTTP {(int)response.StatusCode}. {responseJson}"
            };
        }

        return result ?? new UpdateDesktopTaskDescriptionResult
        {
            Success = false,
            Message = "La API no devolvió una respuesta válida."
        };
    }

    public async Task<UploadDesktopTaskAttachmentResult> UploadAttachmentAsync(
    DesktopTaskItem task,
    string filePath,
    CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            return new UploadDesktopTaskAttachmentResult
            {
                Success = false,
                Message = "El módulo de tareas está deshabilitado."
            };
        }

        if (task.Id == Guid.Empty)
        {
            return new UploadDesktopTaskAttachmentResult
            {
                Success = false,
                Message = "La tarea seleccionada no es válida."
            };
        }

        if (string.IsNullOrWhiteSpace(filePath) ||
            !File.Exists(filePath))
        {
            return new UploadDesktopTaskAttachmentResult
            {
                Success = false,
                Message = "El archivo seleccionado no existe."
            };
        }

        var client =
            BuildClient();

        using var form =
            new MultipartFormDataContent();

        form.Add(
            new StringContent(task.Id.ToString()),
            "taskId"
        );

        form.Add(
            new StringContent(client.UserName),
            "userName"
        );

        form.Add(
            new StringContent(client.DomainName),
            "domainName"
        );

        form.Add(
            new StringContent(client.MachineName),
            "machineName"
        );

        form.Add(
            new StringContent(client.DisplayName),
            "displayName"
        );

        await using var fileStream =
            File.OpenRead(filePath);

        using var fileContent =
            new StreamContent(fileStream);

        form.Add(
            fileContent,
            "file",
            Path.GetFileName(filePath)
        );

        using var response =
            await _httpClient.PostAsync(
                _config.ApiUploadDesktopTaskAttachmentUrl,
                form,
                cancellationToken
            );

        var responseJson =
            await response.Content.ReadAsStringAsync(
                cancellationToken
            );

        var result =
            JsonSerializer.Deserialize<UploadDesktopTaskAttachmentResult>(
                responseJson,
                JsonOptions
            );

        if (!response.IsSuccessStatusCode)
        {
            return result ?? new UploadDesktopTaskAttachmentResult
            {
                Success = false,
                Message = $"Error subiendo adjunto. HTTP {(int)response.StatusCode}. {responseJson}"
            };
        }

        return result ?? new UploadDesktopTaskAttachmentResult
        {
            Success = false,
            Message = "La API no devolvió una respuesta válida."
        };
    }

    public async Task<DownloadDesktopTaskAttachmentResult> DownloadAttachmentAsync(
    DesktopTaskItem task,
    DesktopTaskAttachmentItem attachment,
    CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            return new DownloadDesktopTaskAttachmentResult
            {
                Success = false,
                Message = "El módulo de tareas está deshabilitado."
            };
        }

        var request =
            new DownloadDesktopTaskAttachmentRequest
            {
                Client = BuildClient(),
                TaskId = task.Id,
                AttachmentId = attachment.Id
            };

        var json =
            JsonSerializer.Serialize(request);

        using var content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

        using var response =
            await _httpClient.PostAsync(
                _config.ApiDownloadDesktopTaskAttachmentUrl,
                content,
                cancellationToken
            );

        if (!response.IsSuccessStatusCode)
        {
            var errorText =
                await response.Content.ReadAsStringAsync(
                    cancellationToken
                );

            return new DownloadDesktopTaskAttachmentResult
            {
                Success = false,
                Message = $"Error descargando adjunto. HTTP {(int)response.StatusCode}. {errorText}"
            };
        }

        var fileBytes =
            await response.Content.ReadAsByteArrayAsync(
                cancellationToken
            );

        var fileName =
            response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? attachment.FileName;

        fileName =
            fileName.Trim('"');

        return new DownloadDesktopTaskAttachmentResult
        {
            Success = true,
            Message = "Archivo descargado correctamente.",
            FileName = fileName,
            FileBytes = fileBytes
        };
    }

    public async Task<DeleteDesktopTaskAttachmentResult> DeleteAttachmentAsync(
    DesktopTaskItem task,
    DesktopTaskAttachmentItem attachment,
    CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            return new DeleteDesktopTaskAttachmentResult
            {
                Success = false,
                Message = "El módulo de tareas está deshabilitado."
            };
        }

        var request =
            new DeleteDesktopTaskAttachmentRequest
            {
                Client = BuildClient(),
                TaskId = task.Id,
                AttachmentId = attachment.Id
            };

        var json =
            JsonSerializer.Serialize(request);

        using var content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

        using var response =
            await _httpClient.PostAsync(
                _config.ApiDeleteDesktopTaskAttachmentUrl,
                content,
                cancellationToken
            );

        var responseJson =
            await response.Content.ReadAsStringAsync(
                cancellationToken
            );

        var result =
            JsonSerializer.Deserialize<DeleteDesktopTaskAttachmentResult>(
                responseJson,
                JsonOptions
            );

        if (!response.IsSuccessStatusCode)
        {
            return result ?? new DeleteDesktopTaskAttachmentResult
            {
                Success = false,
                Message = $"Error eliminando adjunto. HTTP {(int)response.StatusCode}. {responseJson}"
            };
        }

        return result ?? new DeleteDesktopTaskAttachmentResult
        {
            Success = false,
            Message = "La API no devolvió una respuesta válida."
        };
    }
}