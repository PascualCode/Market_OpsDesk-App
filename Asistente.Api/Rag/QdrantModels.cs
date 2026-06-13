using System.Text.Json.Serialization;

/// <summary>
/// Petición utilizada para crear una colección vectorial en Qdrant.
/// </summary>
public sealed class QdrantCreateCollectionRequest
{
    [JsonPropertyName("vectors")]
    public QdrantVectorConfiguration Vectors { get; set; } = new();
}


/// <summary>
/// Configuración del vector de una colección Qdrant.
/// </summary>
public sealed class QdrantVectorConfiguration
{
    /// <summary>
    /// Dimensión del vector.
    /// Debe coincidir con el tamaño generado por el modelo de embeddings.
    /// </summary>
    [JsonPropertyName("size")]
    public int Size { get; set; }

    /// <summary>
    /// Distancia usada para comparar vectores.
    /// En nuestro caso usamos similitud coseno.
    /// </summary>
    [JsonPropertyName("distance")]
    public string Distance { get; set; } = "Cosine";
}


/// <summary>
/// Petición de inserción o actualización de puntos vectoriales en Qdrant.
/// </summary>
public sealed class QdrantUpsertRequest
{
    [JsonPropertyName("points")]
    public List<QdrantPoint> Points { get; set; } = [];
}


/// <summary>
/// Punto individual almacenado en Qdrant.
/// Contiene:
/// - ID.
/// - Vector semántico.
/// - Payload con metadatos y texto del fragmento.
/// </summary>
public sealed class QdrantPoint
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("vector")]
    public List<float> Vector { get; set; } = [];

    [JsonPropertyName("payload")]
    public QdrantPayload Payload { get; set; } = new();
}


/// <summary>
/// Metadatos y contenido asociado a cada fragmento indexado.
/// </summary>
public sealed class QdrantPayload
{
    [JsonPropertyName("documentPath")]
    public string DocumentPath { get; set; } = "";

    [JsonPropertyName("documentTitle")]
    public string DocumentTitle { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("chunkIndex")]
    public int ChunkIndex { get; set; }

    [JsonPropertyName("chunkText")]
    public string ChunkText { get; set; } = "";

    [JsonPropertyName("lastModifiedUtc")]
    public DateTime LastModifiedUtc { get; set; }
}


/// <summary>
/// Petición de búsqueda vectorial en Qdrant.
/// </summary>
public sealed class QdrantSearchRequest
{
    [JsonPropertyName("vector")]
    public List<float> Vector { get; set; } = [];

    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 4;

    [JsonPropertyName("with_payload")]
    public bool WithPayload { get; set; } = true;

    [JsonPropertyName("with_vector")]
    public bool WithVector { get; set; } = false;

    [JsonPropertyName("score_threshold")]
    public double? ScoreThreshold { get; set; }
}


/// <summary>
/// Respuesta devuelta por Qdrant al ejecutar una búsqueda vectorial.
/// </summary>
public sealed class QdrantSearchResponse
{
    [JsonPropertyName("result")]
    public List<QdrantScoredPoint> Result { get; set; } = [];
}


/// <summary>
/// Punto recuperado desde Qdrant junto con su puntuación de similitud.
/// </summary>
public sealed class QdrantScoredPoint
{
    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("payload")]
    public QdrantPayload? Payload { get; set; }
}
