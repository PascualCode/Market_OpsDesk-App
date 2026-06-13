/// <summary>
/// Opciones de almacenamiento para la parte de planificación.
/// 
/// DatabasePath indica la ruta del fichero SQLite donde se guardarán
/// tareas y recordatorios.
/// </summary>
public sealed class PlanningStorageOptions
{
    public string DatabasePath { get; set; } =
        "/opt/asistente/data/planning.db";
}


/// <summary>
/// Representa una tarea del usuario.
/// </summary>
public sealed class AssistantTaskItem
{
    /// <summary>
    /// Identificador completo de la tarea.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Clave del usuario propietario.
    /// Ejemplo: dominio\usuario
    /// </summary>
    public string OwnerKey { get; set; } = "";

    /// <summary>
    /// Título breve de la tarea.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Notas opcionales.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Fecha límite opcional, guardada en UTC.
    /// </summary>
    public DateTimeOffset? DueAtUtc { get; set; }

    /// <summary>
    /// Estado de la tarea.
    /// Valores previstos:
    /// - pending
    /// - completed
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// Fecha de creación en UTC.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Fecha de finalización en UTC.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>
    /// Fecha límite local sin hora concreta.
    ///
    /// Se usa cuando el usuario indica un día:
    /// - hoy
    /// - mañana
    /// - el viernes
    ///
    /// pero no especifica una hora exacta.
    ///
    /// Formato previsto: yyyy-MM-dd
    /// </summary>
    public string? DueDateLocal { get; set; }

    /// <summary>
    /// ID corto para mostrar al usuario.
    /// </summary>
    public string ShortId =>
        Id.Length >= 8 ? Id[..8] : Id;
}


/// <summary>
/// Representa un recordatorio o cita del usuario.
/// </summary>
public sealed class AssistantReminderItem
{
    /// <summary>
    /// Identificador completo del recordatorio.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Clave del usuario propietario.
    /// </summary>
    public string OwnerKey { get; set; } = "";

    /// <summary>
    /// Título breve del recordatorio.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Notas opcionales.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Momento en el que debe avisarse, guardado en UTC.
    /// </summary>
    public DateTimeOffset RemindAtUtc { get; set; }

    /// <summary>
    /// Fecha de creación en UTC.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Fecha en la que el recordatorio fue notificado.
    /// La usaremos más adelante cuando el Desktop muestre avisos.
    /// </summary>
    public DateTimeOffset? NotifiedAtUtc { get; set; }

    /// <summary>
    /// Fecha en la que el recordatorio fue descartado o cerrado.
    /// </summary>
    public DateTimeOffset? DismissedAtUtc { get; set; }

    /// <summary>
    /// ID corto para mostrar al usuario.
    /// </summary>
    public string ShortId =>
        Id.Length >= 8 ? Id[..8] : Id;
}