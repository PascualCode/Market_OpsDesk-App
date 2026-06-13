using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Servicio encargado de construir la base vectorial semántica.
///
/// Flujo:
/// 1. Toma documentos desde KnowledgeStore.
/// 2. Los divide en fragmentos.
/// 3. Genera embeddings con Ollama.
/// 4. Recrea la colección de Qdrant.
/// 5. Inserta cada fragmento como punto vectorial.
/// </summary>
public sealed class SemanticKnowledgeIndexer
{
    private readonly KnowledgeStore _knowledgeStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmbeddingOptions _embeddingOptions;
    private readonly QdrantOptions _qdrantOptions;
    private readonly RagIndexingOptions _ragOptions;

    public SemanticKnowledgeIndexer(
        KnowledgeStore knowledgeStore,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _knowledgeStore = knowledgeStore;
        _httpClientFactory = httpClientFactory;

        _embeddingOptions = configuration
            .GetSection("Embeddings")
            .Get<EmbeddingOptions>() ?? new EmbeddingOptions();

        _qdrantOptions = configuration
            .GetSection("Qdrant")
            .Get<QdrantOptions>() ?? new QdrantOptions();

        _ragOptions = configuration
            .GetSection("RagIndexing")
            .Get<RagIndexingOptions>() ?? new RagIndexingOptions();
    }

    /// <summary>
    /// Comprueba si Qdrant responde correctamente.
    /// </summary>
    public async Task<RagStatusResult> GetStatusAsync()
    {
        var qdrantClient = _httpClientFactory.CreateClient("Qdrant");

        try
        {
            var response = await qdrantClient.GetAsync("/collections");

            return new RagStatusResult
            {
                QdrantAvailable = response.IsSuccessStatusCode,
                QdrantBaseUrl = _qdrantOptions.BaseUrl,
                CollectionName = _qdrantOptions.CollectionName,
                Message = response.IsSuccessStatusCode
                    ? "Qdrant está disponible."
                    : $"Qdrant respondió con estado {(int)response.StatusCode}."
            };
        }
        catch (Exception ex)
        {
            return new RagStatusResult
            {
                QdrantAvailable = false,
                QdrantBaseUrl = _qdrantOptions.BaseUrl,
                CollectionName = _qdrantOptions.CollectionName,
                Message = $"No se pudo conectar con Qdrant: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Reconstruye por completo el índice semántico.
    ///
    /// Flujo:
    /// 1. Obtiene documentos desde KnowledgeStore.
    /// 2. Los divide en fragmentos.
    /// 3. Genera embeddings para cada fragmento.
    /// 4. Recrea la colección Qdrant.
    /// 5. Inserta todos los puntos vectoriales.
    /// </summary>
    public async Task<RagReindexResult> ReindexAsync()
    {
        var documents = _knowledgeStore.GetDocuments();

        if (documents.Count == 0)
        {
            return new RagReindexResult
            {
                Message = "No hay documentos de conocimiento cargados.",
                CollectionName = _qdrantOptions.CollectionName
            };
        }

        var chunks = BuildChunks(documents);

        if (chunks.Count == 0)
        {
            return new RagReindexResult
            {
                Message = "No se generaron fragmentos indexables.",
                CollectionName = _qdrantOptions.CollectionName,
                DocumentsProcessed = documents.Count
            };
        }

        var points = new List<QdrantPoint>();
        var vectorSize = 0;

        foreach (var chunk in chunks)
        {
            var embedding = await GenerateEmbeddingAsync(chunk.Text);

            if (embedding.Count == 0)
                continue;

            if (vectorSize == 0)
                vectorSize = embedding.Count;

            points.Add(new QdrantPoint
            {
                Id = Guid.NewGuid().ToString(),
                Vector = embedding,
                Payload = new QdrantPayload
                {
                    DocumentPath = chunk.Document.RelativePath,
                    DocumentTitle = chunk.Document.Title,
                    Category = chunk.Document.Category,
                    ChunkIndex = chunk.Index,
                    ChunkText = chunk.Text,
                    LastModifiedUtc = chunk.Document.LastModifiedUtc
                }
            });
        }

        if (points.Count == 0 || vectorSize == 0)
        {
            return new RagReindexResult
            {
                Message = "No se pudieron generar embeddings válidos.",
                CollectionName = _qdrantOptions.CollectionName,
                DocumentsProcessed = documents.Count,
                ChunksGenerated = chunks.Count
            };
        }

        await RecreateCollectionAsync(vectorSize);
        await UpsertPointsAsync(points);

        return new RagReindexResult
        {
            Message = "Índice semántico reconstruido correctamente.",
            CollectionName = _qdrantOptions.CollectionName,
            DocumentsProcessed = documents.Count,
            ChunksGenerated = chunks.Count,
            PointsInserted = points.Count,
            VectorSize = vectorSize
        };
    }

    /// <summary>
    /// Divide todos los documentos en fragmentos de tamaño controlado.
    /// </summary>
    private List<KnowledgeChunk> BuildChunks(
        List<KnowledgeDocument> documents)
    {
        var chunks = new List<KnowledgeChunk>();

        foreach (var document in documents)
        {
            var text = document.Content.Trim();

            if (string.IsNullOrWhiteSpace(text))
                continue;

            var documentChunks = SplitTextIntoChunks(
                text,
                _ragOptions.ChunkSizeCharacters,
                _ragOptions.ChunkOverlapCharacters
            );

            for (var index = 0; index < documentChunks.Count; index++)
            {
                chunks.Add(new KnowledgeChunk
                {
                    Document = document,
                    Index = index,
                    Text = documentChunks[index]
                });
            }
        }

        return chunks;
    }

    /// <summary>
    /// Fragmenta texto con solape para no perder contexto
    /// entre cortes consecutivos.
    /// </summary>
    private static List<string> SplitTextIntoChunks(
        string text,
        int chunkSize,
        int overlap)
    {
        var chunks = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
            return chunks;

        chunkSize = Math.Max(chunkSize, 300);
        overlap = Math.Clamp(overlap, 0, chunkSize / 2);

        var start = 0;

        while (start < text.Length)
        {
            var length = Math.Min(
                chunkSize,
                text.Length - start
            );

            var chunk = text
                .Substring(start, length)
                .Trim();

            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk);
            }

            if (start + length >= text.Length)
                break;

            start += chunkSize - overlap;
        }

        return chunks;
    }

    /// <summary>
    /// Genera un embedding para un fragmento de texto
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
                $"Error generando embedding con Ollama: " +
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

    /// <summary>
    /// Elimina la colección anterior, si existía,
    /// y crea una nueva con el tamaño de vector correcto.
    /// </summary>
    private async Task RecreateCollectionAsync(int vectorSize)
    {
        var qdrantClient =
            _httpClientFactory.CreateClient("Qdrant");

        var collectionName =
            _qdrantOptions.CollectionName;

        // Intentamos borrar la colección previa.
        // Si no existía, Qdrant puede devolver error,
        // pero continuamos porque después la recreamos.
        await qdrantClient.DeleteAsync(
            $"/collections/{collectionName}"
        );

        var createRequest =
            new QdrantCreateCollectionRequest
            {
                Vectors = new QdrantVectorConfiguration
                {
                    Size = vectorSize,
                    Distance = "Cosine"
                }
            };

        var response = await qdrantClient.PutAsJsonAsync(
            $"/collections/{collectionName}",
            createRequest
        );

        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"No se pudo crear la colección Qdrant: " +
                $"{(int)response.StatusCode} - {json}"
            );
        }
    }

    /// <summary>
    /// Inserta los puntos vectoriales en Qdrant.
    /// 
    /// Cada punto representa un fragmento de una guía interna,
    /// junto con su embedding y sus metadatos.
    /// </summary>
    private async Task UpsertPointsAsync(
        List<QdrantPoint> points)
    {
        var qdrantClient =
            _httpClientFactory.CreateClient("Qdrant");

        var collectionName =
            _qdrantOptions.CollectionName;

        var request = new QdrantUpsertRequest
        {
            Points = points
        };

        var response = await qdrantClient.PutAsJsonAsync(
            $"/collections/{collectionName}/points?wait=true",
            request
        );

        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"No se pudieron insertar puntos en Qdrant: " +
                $"{(int)response.StatusCode} - {json}"
            );
        }
    }
}
