/// <summary>
/// Endpoints relacionados con el motor de herramientas del asistente.
///
/// Incluye:
/// - Chat con herramientas en modo no streaming.
/// - Chat con herramientas en modo streaming, usado por el Desktop.
/// </summary>
public static class ToolEndpoints
{
    /// <summary>
    /// Registra las rutas HTTP del motor de herramientas.
    /// </summary>
    public static WebApplication MapToolEndpoints(
        this WebApplication app)
    {
        // -------------------------------------------------------------
        // POST /api/tools/chat
        // -------------------------------------------------------------
        // Endpoint no streaming del motor de herramientas.
        //
        // Se conserva para:
        // - Pruebas manuales desde PowerShell.
        // - Validar tool calling sin streaming.
        // - Diagnóstico de herramientas y respuestas estructuradas.
        //
        // El avatar Desktop utiliza la variante streaming:
        // POST /api/tools/chat/stream
        // -------------------------------------------------------------
        app.MapPost("/api/tools/chat", async (
            ToolChatRequest request,
            AssistantToolOrchestrator orchestrator,
            HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return Results.BadRequest(new
                {
                    error = "El mensaje no puede estar vacío."
                });
            }

            try
            {
                var result = await orchestrator.ProcessAsync(
                    request,
                    context.RequestAborted
                );

                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    title: "Error procesando el motor de herramientas",
                    detail: ex.Message,
                    statusCode: 500
                );
            }
        });

        // -------------------------------------------------------------
        // POST /api/tools/chat/stream
        // -------------------------------------------------------------
        // Endpoint principal de conversación usado por el avatar Desktop.
        //
        // Integra:
        // - Motor de herramientas.
        // - RAG semántico.
        // - Fallback por palabras clave.
        // - Historial conversacional.
        // - Respuesta en streaming.
        //
        // Es la ruta principal de interacción del asistente empresarial.
        // -------------------------------------------------------------
        app.MapPost("/api/tools/chat/stream", async (
            ToolChatRequest request,
            AssistantToolOrchestrator orchestrator,
            HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                context.Response.StatusCode = 400;

                await context.Response.WriteAsync(
                    "El mensaje no puede estar vacío.",
                    context.RequestAborted
                );

                return;
            }

            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache";

            await orchestrator.StreamProcessAsync(
                request,
                context.Response,
                context.RequestAborted
            );
        });

        return app;
    }
}
