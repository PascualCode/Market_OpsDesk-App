/// <summary>
/// Configuración del modelo de embeddings utilizado
/// para convertir texto en vectores semánticos.
/// </summary>
public sealed class EmbeddingOptions
{
    /// <summary>
    /// Modelo de embeddings utilizado por Ollama.
    /// </summary>
    public string Model { get; set; } = "embeddinggemma";
}
