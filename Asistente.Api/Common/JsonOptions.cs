using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Configuración JSON común utilizada por la API
/// para serializar y deserializar respuestas externas,
/// especialmente Ollama y Qdrant.
/// </summary>
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}