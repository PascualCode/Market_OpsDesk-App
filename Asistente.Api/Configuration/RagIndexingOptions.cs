/// <summary>
/// Configuración del proceso de fragmentación e indexación
/// de documentos dentro del sistema RAG.
/// </summary>
public sealed class RagIndexingOptions
{
    /// <summary>
    /// Tamaño aproximado de cada fragmento documental,
    /// medido en caracteres.
    /// </summary>
    public int ChunkSizeCharacters { get; set; } = 1200;

    /// <summary>
    /// Solapamiento entre fragmentos consecutivos,
    /// medido en caracteres.
    /// </summary>
    public int ChunkOverlapCharacters { get; set; } = 200;
}