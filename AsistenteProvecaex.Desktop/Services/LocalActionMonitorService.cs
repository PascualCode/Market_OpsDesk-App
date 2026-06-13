using System.Windows.Threading;

namespace Asistente.Desktop.Services;

/// <summary>
/// Monitor periódico de acciones locales pendientes.
///
/// Se encarga de:
/// - Consultar la API cada cierto tiempo.
/// - Evitar consultas solapadas.
/// - Ejecutar cada acción mediante LocalActionExecutionService.
/// - Enviar el resultado a la API.
/// </summary>
public sealed class LocalActionMonitorService : IDisposable
{
    private readonly AssistantApiClient _assistantApiClient;
    private readonly LocalActionExecutionService _executionService;
    private readonly DispatcherTimer _timer = new();

    private bool _isCheckingLocalActions;

    public LocalActionMonitorService(
        AssistantApiClient assistantApiClient,
        LocalActionExecutionService executionService,
        int pollingSeconds)
    {
        _assistantApiClient = assistantApiClient;
        _executionService = executionService;

        var safePollingSeconds = Math.Max(
            pollingSeconds,
            5
        );

        _timer.Interval =
            TimeSpan.FromSeconds(safePollingSeconds);

        _timer.Tick += OnTimerTick;
    }

    /// <summary>
    /// Inicia el polling periódico.
    /// </summary>
    public void Start()
    {
        _timer.Start();
    }

    /// <summary>
    /// Detiene el polling periódico.
    /// </summary>
    public void Stop()
    {
        _timer.Stop();
    }

    /// <summary>
    /// Fuerza una comprobación inmediata.
    /// </summary>
    public async Task CheckNowAsync()
    {
        await CheckPendingLocalActionsAsync();
    }

    private async void OnTimerTick(
        object? sender,
        EventArgs e)
    {
        await CheckPendingLocalActionsAsync();
    }

    /// <summary>
    /// Consulta acciones pendientes, las ejecuta
    /// y registra el resultado en la API.
    /// </summary>
    private async Task CheckPendingLocalActionsAsync()
    {
        if (_isCheckingLocalActions)
            return;

        _isCheckingLocalActions = true;

        try
        {
            var pendingResponse =
                await _assistantApiClient.GetPendingLocalActionsAsync();

            if (pendingResponse is null ||
                pendingResponse.Actions.Count == 0)
            {
                return;
            }

            foreach (var action in pendingResponse.Actions)
            {
                var result =
                    _executionService.Execute(action);

                await _assistantApiClient.CompleteLocalActionAsync(
                    action.Id,
                    result.Success,
                    result.Message
                );
            }
        }
        catch
        {
            // El polling de acciones locales nunca debe
            // romper el Desktop.
        }
        finally
        {
            _isCheckingLocalActions = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }
}