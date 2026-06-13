using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Servicio encargado de realizar búsquedas semánticas
/// sobre el conocimiento interno indexado en Qdrant.
///
/// Flujo:
/// 1. Convierte la consulta del usuario en embedding mediante Ollama.
/// 2. Envía ese vector a Qdrant.
/// 3. Recupera los fragmentos documentales más similares.
/// </summary>
public sealed class SemanticKnowledgeSearcher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmbeddingOptions _embeddingOptions;
    private readonly QdrantOptions _qdrantOptions;

    public SemanticKnowledgeSearcher(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;

        _embeddingOptions = configuration
            .GetSection("Embeddings")
            .Get<EmbeddingOptions>() ?? new EmbeddingOptions();

        _qdrantOptions = configuration
            .GetSection("Qdrant")
            .Get<QdrantOptions>() ?? new QdrantOptions();
    }

    /// <summary>
    /// Ejecuta una búsqueda semántica sobre la colección de Qdrant.
    ///
    /// Parámetros:
    /// - query: consulta del usuario.
    /// - maxResults: número máximo de fragmentos a devolver.
    /// - scoreThreshold: similitud mínima opcional.
    /// </summary>
    public async Task<List<SemanticKnowledgeSearchResult>> SearchAsync(
        string query,
        int maxResults,
        double? scoreThreshold = null)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var embedding = await GenerateEmbeddingAsync(query);

        if (embedding.Count == 0)
            return [];

        var qdrantClient =
            _httpClientFactory.CreateClient("Qdrant");

        var request = new QdrantSearchRequest
        {
            Vector = embedding,
            Limit = Math.Clamp(maxResults, 1, 10),
            WithPayload = true,
            WithVector = false,
            ScoreThreshold = scoreThreshold
        };

        var response = await qdrantClient.PostAsJsonAsync(
            $"/collections/{_qdrantOptions.CollectionName}/points/search",
            request
        );

        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Error buscando en Qdrant: " +
                $"{(int)response.StatusCode} - {json}"
            );
        }

        var qdrantResponse =
            JsonSerializer.Deserialize<QdrantSearchResponse>(
                json,
                JsonOptions.Default
            );

        return qdrantResponse?.Result
            .Where(point => point.Payload is not null)
            .Select(point => new SemanticKnowledgeSearchResult
            {
                RelativePath = point.Payload!.DocumentPath,
                Category = point.Payload.Category,
                Title = point.Payload.DocumentTitle,
                ChunkIndex = point.Payload.ChunkIndex,
                ChunkText = point.Payload.ChunkText,
                Score = point.Score
            })
            .ToList() ?? [];
    }

    /// <summary>
    /// Genera el embedding semántico de la consulta del usuario
    /// mediante Ollama /api/embed.
    /// </summary>
    private async Task<List<float>> GenerateEmbeddingAsync(string text)
    {
        var ollamaClient =
            _httpClientFactory.CreateClient("Ollama");

        var request = new OllamaEmbedRequest
        {
            Model = _embeddingOptions.Model,
            Input = text
        };

        var response = await ollamaClient.PostAsJsonAsync(
            "/api/embed",
            request
        );

        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Error generando embedding de consulta: " +
                $"{(int)response.StatusCode} - {json}"
            );
        }

        var embedResponse =
            JsonSerializer.Deserialize<OllamaEmbedResponse>(
                json,
                JsonOptions.Default
            );

        return embedResponse?.Embeddings.FirstOrDefault() ?? [];
    }
}