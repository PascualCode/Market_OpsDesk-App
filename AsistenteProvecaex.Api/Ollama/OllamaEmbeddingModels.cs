using System.Text.Json.Serialization;

/// <summary>
/// Petición enviada a Ollama para generar embeddings
/// a partir de un texto.
/// 
/// Se utiliza tanto para:
/// - Indexar fragmentos de conocimiento.
/// - Convertir consultas del usuario en vectores de búsqueda.
/// </summary>
public sealed class OllamaEmbedRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("input")]
    public string Input { get; set; } = "";
}


/// <summary>
/// Respuesta devuelta por Ollama al solicitar embeddings.
/// 
/// Ollama devuelve una lista de vectores, aunque en nuestro caso
/// normalmente enviamos un único texto y usamos el primer vector.
/// </summary>
public sealed class OllamaEmbedResponse
{
    [JsonPropertyName("embeddings")]
    public List<List<float>> Embeddings { get; set; } = [];
}