/// <summary>
/// Configuración de acceso a Qdrant,
/// la base vectorial usada para RAG semántico.
/// </summary>
public sealed class QdrantOptions
{
    /// <summary>
    /// URL base del servicio Qdrant.
    /// </summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:6335";

    /// <summary>
    /// Nombre de la colección vectorial que almacena
    /// los fragmentos de conocimiento interno.
    /// </summary>
    public string CollectionName { get; set; } =
        "knowledge_chunks";
}
