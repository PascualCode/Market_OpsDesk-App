using Asistente.Desktop.Models;
using System.Windows.Threading;

namespace Asistente.Desktop.Services;

/// <summary>
/// Monitor de recordatorios vencidos.
///
/// Se encarga de:
/// - Consultar periódicamente la API.
/// - Evitar comprobaciones simultáneas.
/// - Entregar cada recordatorio a la UI para mostrarlo.
/// - Marcarlo como notificado después.
/// </summary>
public sealed class ReminderMonitorService : IDisposable
{
    private readonly AssistantApiClient _assistantApiClient;
    private readonly Func<DueReminderDesktopItem, Task> _onReminderDue;
    private readonly DispatcherTimer _timer = new();

    private bool _isCheckingReminders;

    public ReminderMonitorService(
        AssistantApiClient assistantApiClient,
        int pollingSeconds,
        Func<DueReminderDesktopItem, Task> onReminderDue)
    {
        _assistantApiClient = assistantApiClient;
        _onReminderDue = onReminderDue;

        var safePollingSeconds = Math.Max(
            pollingSeconds,
            10
        );

        _timer.Interval =
            TimeSpan.FromSeconds(safePollingSeconds);

        _timer.Tick += OnTimerTick;
    }

    /// <summary>
    /// Inicia el temporizador periódico.
    /// </summary>
    public void Start()
    {
        _timer.Start();
    }

    /// <summary>
    /// Detiene el temporizador periódico.
    /// </summary>
    public void Stop()
    {
        _timer.Stop();
    }

    /// <summary>
    /// Fuerza una comprobación inmediata.
    /// Útil al arrancar el Desktop.
    /// </summary>
    public async Task CheckNowAsync()
    {
        await CheckDueRemindersAsync();
    }

    /// <summary>
    /// Tick periódico del DispatcherTimer.
    /// </summary>
    private async void OnTimerTick(
        object? sender,
        EventArgs e)
    {
        await CheckDueRemindersAsync();
    }

    /// <summary>
    /// Consulta la API, muestra cada recordatorio mediante callback
    /// y lo marca después como notificado.
    /// </summary>
    private async Task CheckDueRemindersAsync()
    {
        if (_isCheckingReminders)
            return;

        _isCheckingReminders = true;

        try
        {
            var dueReminders =
                await _assistantApiClient.GetDueRemindersAsync();

            if (dueReminders is null ||
                dueReminders.Count == 0)
            {
                return;
            }

            foreach (var reminder in dueReminders.Reminders)
            {
                await _onReminderDue(reminder);

                await _assistantApiClient.MarkReminderAsNotifiedAsync(
                    reminder.Id
                );
            }
        }
        catch
        {
            // El monitor de recordatorios no debe romper el Desktop.
        }
        finally
        {
            _isCheckingReminders = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }
}
