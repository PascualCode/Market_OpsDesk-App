/// <summary>
/// Endpoints utilizados por el Desktop para:
/// - Consultar acciones locales pendientes.
/// - Informar del resultado de su ejecución.
/// </summary>
public static class LocalActionEndpoints
{
    /// <summary>
    /// Registra las rutas HTTP del módulo de acciones locales.
    /// </summary>
    public static WebApplication MapLocalActionEndpoints(
        this WebApplication app)
    {
        // -------------------------------------------------------------
        // POST /api/local-actions/pending
        // -------------------------------------------------------------
        // Devuelve acciones locales pendientes para:
        // - Usuario actual.
        // - Equipo actual.
        //
        // El Desktop consultará este endpoint periódicamente.
        // -------------------------------------------------------------
        app.MapPost("/api/local-actions/pending", async (
            PendingLocalActionsRequest request,
            LocalActionRepository repository,
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

            if (string.IsNullOrWhiteSpace(request.Client.MachineName))
            {
                return Results.BadRequest(new
                {
                    error = "No se pudo identificar el equipo cliente."
                });
            }

            var actions = await repository.GetPendingActionsAsync(
                executionContext.OwnerKey,
                request.Client.MachineName,
                maxResults: 10,
                context.RequestAborted
            );

            return Results.Ok(new
            {
                count = actions.Count,
                actions = actions.Select(action => new
                {
                    id = action.Id,
                    shortId = action.ShortId,
                    actionType = action.ActionType,
                    target = action.Target,
                    createdAtUtc = action.CreatedAtUtc
                })
            });
        });

        // -------------------------------------------------------------
        // POST /api/local-actions/complete
        // -------------------------------------------------------------
        // Marca una acción local como:
        // - completed
        // - failed
        //
        // El Desktop llama a este endpoint después de intentar
        // ejecutar la acción recibida.
        // -------------------------------------------------------------
        app.MapPost("/api/local-actions/complete", async (
            CompleteLocalActionRequest request,
            LocalActionRepository repository,
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

            if (string.IsNullOrWhiteSpace(request.Client.MachineName))
            {
                return Results.BadRequest(new
                {
                    error = "No se pudo identificar el equipo cliente."
                });
            }

            if (string.IsNullOrWhiteSpace(request.ActionId))
            {
                return Results.BadRequest(new
                {
                    error = "No se ha recibido un identificador de acción válido."
                });
            }

            await repository.CompleteActionAsync(
                request.ActionId,
                executionContext.OwnerKey,
                request.Client.MachineName,
                request.Success,
                request.ResultMessage,
                context.RequestAborted
            );

            return Results.Ok(new
            {
                message = "Resultado de la acción local registrado correctamente."
            });
        });

        return app;
    }
}