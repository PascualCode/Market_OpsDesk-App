using System.Text.Json;
using System.Text.Json.Serialization;

// =============================================================
// MODELOS DE TOOL CALLING PARA OLLAMA
// =============================================================

/// <summary>
/// Definición completa de una herramienta que se envía a Ollama.
/// </summary>
public sealed class OllamaToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OllamaFunctionDefinition Function { get; set; } = new();
}


/// <summary>
/// Descripción de la función que el modelo puede solicitar ejecutar.
/// </summary>
public sealed class OllamaFunctionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("parameters")]
    public OllamaFunctionParameters Parameters { get; set; } = new();
}


/// <summary>
/// Esquema de parámetros de una herramienta.
/// </summary>
public sealed class OllamaFunctionParameters
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("required")]
    public List<string> Required { get; set; } = [];

    [JsonPropertyName("properties")]
    public Dictionary<string, OllamaFunctionParameterProperty> Properties { get; set; } = [];
}


/// <summary>
/// Definición de un parámetro individual dentro del esquema de una herramienta.
/// </summary>
public sealed class OllamaFunctionParameterProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}


/// <summary>
/// Solicitud de ejecución de herramienta devuelta por Ollama.
/// </summary>
public sealed class OllamaToolCall
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OllamaToolCallFunction Function { get; set; } = new();
}


/// <summary>
/// Información concreta de la función que Ollama solicita ejecutar.
/// </summary>
public sealed class OllamaToolCallFunction
{
    [JsonPropertyName("index")]
    public int? Index { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; set; }
}
