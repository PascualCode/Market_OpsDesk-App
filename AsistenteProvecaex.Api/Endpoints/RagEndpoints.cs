/// <summary>
/// Endpoints relacionados con el sistema RAG semántico.
///
/// Incluye:
/// - Comprobación de estado de Qdrant.
/// - Reindexación completa de guías.
/// - Búsqueda semántica de prueba.
/// </summary>
public static class RagEndpoints
{
    /// <summary>
    /// Registra las rutas HTTP del módulo RAG.
    /// </summary>
    public static WebApplication MapRagEndpoints(
        this WebApplication app)
    {
        // -------------------------------------------------------------
        // GET /api/rag/status
        // -------------------------------------------------------------
        // Comprueba si Qdrant está accesible y devuelve información
        // básica de la colección vectorial de conocimiento.
        // -------------------------------------------------------------
        app.MapGet("/api/rag/status", async (
            SemanticKnowledgeIndexer semanticIndexer) =>
        {
            var status = await semanticIndexer.GetStatusAsync();

            return Results.Ok(status);
        });

        // -------------------------------------------------------------
        // POST /api/rag/reindex
        // -------------------------------------------------------------
        // Reconstruye desde cero la colección semántica:
        //
        // 1. Lee las guías cargadas en KnowledgeStore.
        // 2. Las fragmenta.
        // 3. Genera embeddings con Ollama.
        // 4. Recrea la colección de Qdrant.
        // 5. Inserta cada fragmento con su vector y metadatos.
        // -------------------------------------------------------------
        app.MapPost("/api/rag/reindex", async (
            SemanticKnowledgeIndexer semanticIndexer) =>
        {
            var result = await semanticIndexer.ReindexAsync();

            return Results.Ok(result);
        });

        // -------------------------------------------------------------
        // POST /api/rag/search
        // -------------------------------------------------------------
        // Permite probar directamente la recuperación semántica:
        //
        // Consulta -> embedding -> Qdrant -> fragmentos recuperados.
        // -------------------------------------------------------------
        app.MapPost("/api/rag/search", async (
            SemanticKnowledgeSearchRequest request,
            SemanticKnowledgeSearcher semanticSearcher,
            IConfiguration configuration) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return Results.BadRequest(new
                {
                    error = "La consulta semántica no puede estar vacía."
                });
            }

            var options = configuration
                .GetSection("RagSearch")
                .Get<RagSearchOptions>() ?? new RagSearchOptions();

            var maxResults = request.MaxResults <= 0
                ? options.TopK
                : Math.Min(request.MaxResults, 10);

            var threshold =
                request.ScoreThreshold ?? options.ScoreThreshold;

            try
            {
                var results = await semanticSearcher.SearchAsync(
                    request.Query,
                    maxResults,
                    threshold
                );

                return Results.Ok(new
                {
                    query = request.Query,
                    resultCount = results.Count,
                    results
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    title: "Error ejecutando búsqueda semántica",
                    detail: ex.Message,
                    statusCode: 500
                );
            }
        });

        return app;
    }
}