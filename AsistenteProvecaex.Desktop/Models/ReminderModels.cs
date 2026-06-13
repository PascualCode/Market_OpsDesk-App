namespace Asistente.Desktop.Models;

/// <summary>
/// Petición enviada al endpoint /api/reminders/due.
/// </summary>
public sealed class DueRemindersDesktopRequest
{
    public DesktopClientContext Client { get; set; } = new();
}

/// <summary>
/// Respuesta recibida desde la API con recordatorios
/// pendientes de mostrar al usuario.
/// </summary>
public sealed class DueRemindersDesktopResponse
{
    public int Count { get; set; }

    public List<DueReminderDesktopItem> Reminders { get; set; } = [];
}

/// <summary>
/// Recordatorio individual recibido desde la API.
/// </summary>
public sealed class DueReminderDesktopItem
{
    public string Id { get; set; } = "";

    public string ShortId { get; set; } = "";

    public string Title { get; set; } = "";

    public string? Notes { get; set; }

    public DateTimeOffset RemindAtUtc { get; set; }
}

/// <summary>
/// Petición enviada al endpoint /api/reminders/mark-notified.
/// </summary>
public sealed class MarkReminderNotifiedDesktopRequest
{
    public DesktopClientContext Client { get; set; } = new();

    public string ReminderId { get; set; } = "";
}