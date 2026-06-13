/// <summary>
/// Endpoints relacionados con la consulta y marcado
/// de recordatorios que deben notificarse en el Desktop.
/// </summary>
public static class ReminderEndpoints
{
    /// <summary>
    /// Registra las rutas HTTP de recordatorios.
    /// </summary>
    public static WebApplication MapReminderEndpoints(
        this WebApplication app)
    {
        // -------------------------------------------------------------
        // POST /api/reminders/due
        // -------------------------------------------------------------
        // Devuelve recordatorios cuyo momento de aviso ya ha llegado
        // y que todavía no han sido notificados al usuario.
        // -------------------------------------------------------------
        app.MapPost("/api/reminders/due", async (
            DueRemindersRequest request,
            PlanningRepository repository,
            HttpContext context) =>
        {
            var executionContext =
                new AssistantToolExecutionContext(request.Client);

            if (!executionContext.HasIdentifiedUser)
            {
                return Results.BadRequest(new
                {
                    error = "No se pudo identificar al usuario."
                });
            }

            var reminders =
                await repository.GetDueUnnotifiedRemindersAsync(
                    executionContext.OwnerKey,
                    maxResults: 10,
                    context.RequestAborted
                );

            return Results.Ok(new
            {
                count = reminders.Count,
                reminders = reminders.Select(reminder => new
                {
                    id = reminder.Id,
                    shortId = reminder.ShortId,
                    title = reminder.Title,
                    notes = reminder.Notes,
                    remindAtUtc = reminder.RemindAtUtc
                })
            });
        });

        // -------------------------------------------------------------
        // POST /api/reminders/mark-notified
        // -------------------------------------------------------------
        // Marca un recordatorio como ya mostrado al usuario,
        // para evitar que vuelva a notificarse continuamente.
        // -------------------------------------------------------------
        app.MapPost("/api/reminders/mark-notified", async (
            MarkReminderNotifiedRequest request,
            PlanningRepository repository,
            HttpContext context) =>
        {
            var executionContext =
                new AssistantToolExecutionContext(request.Client);

            if (!executionContext.HasIdentifiedUser)
            {
                return Results.BadRequest(new
                {
                    error = "No se pudo identificar al usuario."
                });
            }

            if (string.IsNullOrWhiteSpace(request.ReminderId))
            {
                return Results.BadRequest(new
                {
                    error =
                        "No se ha recibido un identificador de recordatorio válido."
                });
            }

            await repository.MarkReminderAsNotifiedAsync(
                request.ReminderId,
                executionContext.OwnerKey,
                context.RequestAborted
            );

            return Results.Ok(new
            {
                message = "Recordatorio marcado como notificado."
            });
        });

        return app;
    }
}