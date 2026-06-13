using Asistente.Desktop.Configuration;
using Asistente.Desktop.Infrastructure;
using Asistente.Desktop.Models;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Asistente.Desktop.Services;

/// <summary>
/// Cliente de comunicación entre el Desktop y la API.
///
/// Centraliza llamadas HTTP que antes vivían en MainWindow:
/// - Health check.
/// - Recordatorios vencidos.
/// - Marcado de recordatorios notificados.
/// - Acciones locales pendientes.
/// - Confirmación de acciones locales.
/// </summary>
public sealed class AssistantApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AppConfig _config;
    private readonly DesktopClientContextService _desktopClientContextService;

    public AssistantApiClient(
        HttpClient httpClient,
        AppConfig config,
        DesktopClientContextService desktopClientContextService)
    {
        _httpClient = httpClient;
        _config = config;
        _desktopClientContextService = desktopClientContextService;
    }

    /// <summary>
    /// Comprueba si la API está disponible.
    /// </summary>
    public async Task<ApiHealthCheckResult> CheckHealthAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                _config.ApiHealthUrl,
                cancellationToken
            );

            return new ApiHealthCheckResult
            {
                IsAvailable = response.IsSuccessStatusCode,
                StatusCode = (int)response.StatusCode
            };
        }
        catch
        {
            return new ApiHealthCheckResult
            {
                IsAvailable = false,
                StatusCode = null
            };
        }
    }

    /// <summary>
    /// Consulta recordatorios vencidos pendientes de notificación.
    /// </summary>
    public async Task<DueRemindersDesktopResponse?> GetDueRemindersAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new DueRemindersDesktopRequest
            {
                Client = _desktopClientContextService.Build()
            };

            var json = JsonSerializer.Serialize(
                request,
                JsonOptions.Default
            );

            using var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

            using var response = await _httpClient.PostAsync(
                _config.ApiDueRemindersUrl,
                content,
                cancellationToken
            );

            if (!response.IsSuccessStatusCode)
                return null;

            var responseJson = await response.Content.ReadAsStringAsync(
                cancellationToken
            );

            return JsonSerializer.Deserialize<DueRemindersDesktopResponse>(
                responseJson,
                JsonOptions.Default
            );
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Informa a la API de que un recordatorio ya se ha mostrado.
    /// </summary>
    public async Task<bool> MarkReminderAsNotifiedAsync(
        string reminderId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new MarkReminderNotifiedDesktopRequest
            {
                Client = _desktopClientContextService.Build(),
                ReminderId = reminderId
            };

            var json = JsonSerializer.Serialize(
                request,
                JsonOptions.Default
            );

            using var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

            using var response = await _httpClient.PostAsync(
                _config.ApiMarkReminderNotifiedUrl,
                content,
                cancellationToken
            );

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Consulta acciones locales pendientes para este usuario y equipo.
    /// </summary>
    public async Task<PendingLocalActionsResponse?> GetPendingLocalActionsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new PendingLocalActionsDesktopRequest
            {
                Client = _desktopClientContextService.Build()
            };

            using var response = await _httpClient.PostAsJsonAsync(
                _config.ApiPendingLocalActionsUrl,
                request,
                JsonOptions.Default,
                cancellationToken
            );

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<PendingLocalActionsResponse>(
                JsonOptions.Default,
                cancellationToken
            );
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Informa a la API del resultado final de una acción local procesada.
    /// </summary>
    public async Task<bool> CompleteLocalActionAsync(
        string actionId,
        bool success,
        string? resultMessage,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new CompleteLocalActionDesktopRequest
            {
                Client = _desktopClientContextService.Build(),
                ActionId = actionId,
                Success = success,
                ResultMessage = resultMessage
            };

            using var response = await _httpClient.PostAsJsonAsync(
                _config.ApiCompleteLocalActionUrl,
                request,
                JsonOptions.Default,
                cancellationToken
            );

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
