using System.Text;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Servicio encargado de construir el contexto documental
/// que se entrega al modelo de lenguaje.
///
/// Estrategia:
/// 1. Intenta recuperar fragmentos mediante RAG semántico.
/// 2. Si no hay resultados válidos o falla, usa búsqueda por palabras clave.
/// 3. Construye un bloque de contexto legible para el prompt.
/// 4. Permite enriquecer el System Prompt del asistente.
/// </summary>
public sealed class KnowledgeContextService
{
    private readonly KnowledgeStore _knowledgeStore;
    private readonly SemanticKnowledgeSearcher _semanticSearcher;
    private readonly IConfiguration _configuration;

    public KnowledgeContextService(
        KnowledgeStore knowledgeStore,
        SemanticKnowledgeSearcher semanticSearcher,
        IConfiguration configuration)
    {
        _knowledgeStore = knowledgeStore;
        _semanticSearcher = semanticSearcher;
        _configuration = configuration;
    }

    /// <summary>
    /// Construye el contexto de conocimiento más adecuado
    /// para la consulta del usuario.
    ///
    /// Prioridad:
    /// - Búsqueda semántica RAG.
    /// - Fallback por palabras clave.
    /// </summary>
    public async Task<KnowledgePromptContext> BuildContextAsync(
        string userQuery,
        CancellationToken cancellationToken = default)
    {
        var ragOptions = _configuration
            .GetSection("RagSearch")
            .Get<RagSearchOptions>() ?? new RagSearchOptions();

        if (!ragOptions.Enabled)
        {
            return BuildKeywordFallbackContext(userQuery);
        }

        try
        {
            var semanticResults = await _semanticSearcher.SearchAsync(
                userQuery,
                ragOptions.TopK,
                ragOptions.ScoreThreshold
            );

            if (semanticResults.Count > 0)
            {
                var context = BuildSemanticContext(
                    semanticResults,
                    ragOptions.MaxContextCharacters
                );

                if (context.HasContext)
                    return context;
            }
        }
        catch
        {
            // Si la búsqueda semántica falla,
            // se intentará usar fallback por palabras clave.
        }

        if (ragOptions.UseKeywordFallback)
        {
            return BuildKeywordFallbackContext(userQuery);
        }

        return new KnowledgePromptContext();
    }

    /// <summary>
    /// Añade documentación interna recuperada al prompt base.
    ///
    /// Si no hay contexto documental, devuelve el prompt original.
    /// </summary>
    public string BuildSystemPromptWithKnowledge(
        string baseSystemPrompt,
        KnowledgePromptContext knowledgeContext)
    {
        if (!knowledgeContext.HasContext)
            return baseSystemPrompt;

        return $"""
        {baseSystemPrompt}

        A continuación se incluye documentación interna de Supermercado
        recuperada automáticamente porque parece relevante para la consulta del usuario.

        INSTRUCCIONES PARA USAR ESTA DOCUMENTACIÓN:
        - Prioriza la documentación interna frente a conocimiento general.
        - Si la documentación responde a la pregunta, basa la respuesta en ella.
        - Si la documentación es insuficiente, dilo de forma clara.
        - No inventes procedimientos internos que no aparezcan en la documentación.
        - Responde de manera útil, clara y ordenada para el usuario.

        DOCUMENTACIÓN INTERNA RECUPERADA:
        {knowledgeContext.ContextText}
        """;
    }

    /// <summary>
    /// Construye el contexto documental a partir
    /// de fragmentos recuperados por búsqueda semántica RAG.
    /// </summary>
    private static KnowledgePromptContext BuildSemanticContext(
        List<SemanticKnowledgeSearchResult> semanticResults,
        int maxContextCharacters)
    {
        var context = new KnowledgePromptContext();
        var sb = new StringBuilder();

        foreach (var result in semanticResults)
        {
            var header =
                $"### Fragmento interno recuperado\n" +
                $"Documento: {result.Title}\n" +
                $"Ruta: {result.RelativePath}\n" +
                $"Categoría: {result.Category}\n\n";

            if (sb.Length + header.Length >= maxContextCharacters)
                break;

            sb.AppendLine(header);

            var remainingCharacters =
                maxContextCharacters - sb.Length;

            var chunk = result.ChunkText.Trim();

            if (chunk.Length > remainingCharacters)
            {
                chunk = chunk[..remainingCharacters];
                chunk += "\n[Fragmento truncado por límite de contexto]";
            }

            sb.AppendLine(chunk);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            context.Results.Add(new KnowledgeSearchResult
            {
                RelativePath = result.RelativePath,
                Category = result.Category,
                Title = result.Title,
                Score = (int)Math.Round(result.Score * 100),
                Preview = result.ChunkText.Length > 220
                    ? result.ChunkText[..220] + "..."
                    : result.ChunkText
            });
        }

        context.ContextText = sb.ToString().Trim();

        return context;
    }

    /// <summary>
    /// Construye contexto documental mediante
    /// búsqueda tradicional por palabras clave.
    ///
    /// Se utiliza cuando:
    /// - El RAG está desactivado.
    /// - La búsqueda semántica falla.
    /// - No se recuperan fragmentos relevantes.
    /// </summary>
    private KnowledgePromptContext BuildKeywordFallbackContext(
        string userQuery)
    {
        var options = _configuration
            .GetSection("Knowledge")
            .Get<KnowledgeOptions>() ?? new KnowledgeOptions();

        var result = new KnowledgePromptContext();

        if (!options.Enabled)
            return result;

        if (string.IsNullOrWhiteSpace(userQuery))
            return result;

        var searchResults = _knowledgeStore
            .Search(userQuery, options.MaxDocumentsForPrompt)
            .Where(searchResult =>
                searchResult.Score >= options.MinScoreForPrompt)
            .ToList();

        if (searchResults.Count == 0)
            return result;

        var documentsByPath = _knowledgeStore
            .GetDocuments()
            .ToDictionary(
                document => document.RelativePath,
                document => document,
                StringComparer.OrdinalIgnoreCase
            );

        var sb = new StringBuilder();

        foreach (var searchResult in searchResults)
        {
            if (!documentsByPath.TryGetValue(
                    searchResult.RelativePath,
                    out var document))
            {
                continue;
            }

            var remainingCharacters =
                options.MaxContextCharacters - sb.Length;

            if (remainingCharacters <= 0)
                break;

            var header =
                $"### Documento interno: {document.Title}\n" +
                $"Ruta: {document.RelativePath}\n\n";

            if (header.Length >= remainingCharacters)
                break;

            sb.AppendLine(header);

            remainingCharacters =
                options.MaxContextCharacters - sb.Length;

            var documentContent =
                document.Content.Trim();

            if (documentContent.Length > remainingCharacters)
            {
                documentContent =
                    documentContent[..remainingCharacters];

                documentContent +=
                    "\n[Contenido truncado por límite de contexto]";
            }

            sb.AppendLine(documentContent);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        result.ContextText = sb.ToString().Trim();
        result.Results = searchResults;

        return result;
    }
}