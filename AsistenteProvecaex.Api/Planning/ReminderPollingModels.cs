/// <summary>
/// Petición para consultar recordatorios vencidos del usuario actual.
/// </summary>
public sealed class DueRemindersRequest
{
    public AssistantClientContext Client { get; set; } = new();
}


/// <summary>
/// Petición para marcar un recordatorio como ya notificado.
/// </summary>
public sealed class MarkReminderNotifiedRequest
{
    public AssistantClientContext Client { get; set; } = new();

    public string ReminderId { get; set; } = "";
}