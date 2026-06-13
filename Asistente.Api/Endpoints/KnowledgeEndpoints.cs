/// <summary>
/// Endpoints relacionados con la base de conocimiento documental.
///
/// Incluye:
/// - Recarga manual de guías desde disco.
/// - Búsqueda por palabras clave para diagnóstico.
/// </summary>
public static class KnowledgeEndpoints
{
    /// <summary>
    /// Registra las rutas HTTP del módulo de conocimiento.
    /// </summary>
    public static WebApplication MapKnowledgeEndpoints(
        this WebApplication app)
    {
        // -------------------------------------------------------------
        // POST /api/knowledge/reload
        // -------------------------------------------------------------
        // Recarga las guías .md/.txt desde la carpeta knowledge
        // sin necesidad de reiniciar la API.
        //
        // Importante:
        // - Refresca KnowledgeStore.
        // - No reconstruye automáticamente el índice RAG de Qdrant.
        //   Para eso se utiliza /api/rag/reindex.
        // -------------------------------------------------------------
        app.MapPost("/api/knowledge/reload", (
            KnowledgeStore knowledgeStore) =>
        {
            knowledgeStore.Reload();

            var documents = knowledgeStore.GetDocuments();

            return Results.Ok(new
            {
                message = "Base de conocimiento recargada correctamente.",
                enabled = knowledgeStore.Enabled,
                directory = knowledgeStore.Directory,
                documentCount = documents.Count,
                documents = documents.Select(document => new
                {
                    document.RelativePath,
                    document.Category,
                    document.Title,
                    document.LastModifiedUtc
                })
            });
        });

        // -------------------------------------------------------------
        // POST /api/knowledge/search
        // -------------------------------------------------------------
        // Permite probar la búsqueda por palabras clave
        // sobre las guías cargadas en memoria.
        //
        // Este endpoint se mantiene como diagnóstico y fallback
        // frente a la búsqueda semántica RAG.
        // -------------------------------------------------------------
        app.MapPost("/api/knowledge/search", (
            KnowledgeSearchRequest request,
            KnowledgeStore knowledgeStore) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return Results.BadRequest(new
                {
                    error = "La consulta de búsqueda no puede estar vacía."
                });
            }

            var maxResults = request.MaxResults <= 0
                ? 3
                : Math.Min(request.MaxResults, 10);

            var results = knowledgeStore.Search(
                request.Query,
                maxResults
            );

            return Results.Ok(new
            {
                query = request.Query,
                resultCount = results.Count,
                results
            });
        });

        return app;
    }
}
