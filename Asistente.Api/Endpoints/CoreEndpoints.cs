using System.Net.Http.Json;
using System.Text.Json;

/// <summary>
/// Endpoints base de la API.
///
/// Incluye:
/// - Ruta raíz.
/// - Health check.
/// - Consulta de modelos disponibles en Ollama.
/// </summary>
public static class CoreEndpoints
{
    /// <summary>
    /// Registra las rutas base de la API.
    /// </summary>
    public static WebApplication MapCoreEndpoints(
        this WebApplication app)
    {
        // -------------------------------------------------------------
        // GET /
        // -------------------------------------------------------------
        // Ruta de comprobación básica para saber que la API responde.
        // -------------------------------------------------------------
        app.MapGet("/", () =>
        {
            return Results.Ok(new
            {
                service = "Asistente.Api",
                status = "running"
            });
        });

        // -------------------------------------------------------------
        // GET /health
        // -------------------------------------------------------------
        // Endpoint ligero que utiliza el Desktop para comprobar
        // si la API está accesible.
        // -------------------------------------------------------------
        app.MapGet("/health", () =>
        {
            return Results.Ok(new
            {
                status = "ok",
                service = "Asistente.Api",
                timestamp = DateTime.Now
            });
        });

        // -------------------------------------------------------------
        // GET /api/models
        // -------------------------------------------------------------
        // Consulta a Ollama los modelos disponibles.
        // Útil para diagnóstico y comprobaciones técnicas.
        // -------------------------------------------------------------
        app.MapGet("/api/models", async (
            IHttpClientFactory httpClientFactory) =>
        {
            var ollamaClient =
                httpClientFactory.CreateClient("Ollama");

            var response =
                await ollamaClient.GetAsync("/api/tags");

            var json =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return Results.Problem(
                    title: "Error consultando modelos de Ollama",
                    detail:
                        $"Ollama respondió con estado {(int)response.StatusCode}: {json}",
                    statusCode: 500
                );
            }

            var payload =
                JsonSerializer.Deserialize<JsonElement>(
                    json,
                    JsonOptions.Default
                );

            return Results.Ok(payload);
        });

        return app;
    }
}