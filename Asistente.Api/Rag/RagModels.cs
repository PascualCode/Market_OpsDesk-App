/// <summary>
/// Resultado devuelto tras reconstruir el índice semántico RAG.
/// </summary>
public sealed class RagReindexResult
{
    public string Message { get; set; } = "";

    public string CollectionName { get; set; } = "";

    public int DocumentsProcessed { get; set; }

    public int ChunksGenerated { get; set; }

    public int PointsInserted { get; set; }

    public int VectorSize { get; set; }
}


/// <summary>
/// Estado básico del sistema RAG y de la conexión con Qdrant.
/// </summary>
public sealed class RagStatusResult
{
    public bool QdrantAvailable { get; set; }

    public string QdrantBaseUrl { get; set; } = "";

    public string CollectionName { get; set; } = "";

    public string Message { get; set; } = "";
}


/// <summary>
/// Petición para probar una búsqueda semántica sobre Qdrant.
/// </summary>
public sealed class SemanticKnowledgeSearchRequest
{
    public string Query { get; set; } = "";

    public int MaxResults { get; set; } = 4;

    public double? ScoreThreshold { get; set; }
}


/// <summary>
/// Fragmento documental recuperado mediante búsqueda semántica.
/// </summary>
public sealed class SemanticKnowledgeSearchResult
{
    public string RelativePath { get; set; } = "";

    public string Category { get; set; } = "";

    public string Title { get; set; } = "";

    public int ChunkIndex { get; set; }

    public string ChunkText { get; set; } = "";

    public double Score { get; set; }
}


/// <summary>
/// Fragmento individual de un documento interno.
///
/// Se usa durante la indexación semántica para convertir
/// documentos largos en bloques manejables.
/// </summary>
public sealed class KnowledgeChunk
{
    public KnowledgeDocument Document { get; set; } = new();

    public int Index { get; set; }

    public string Text { get; set; } = "";
}
