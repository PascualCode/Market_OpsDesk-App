using System.Text.Json.Serialization;

/// <summary>
/// Contenido completo del fichero JSON del directorio telefónico.
/// </summary>
public sealed class PhoneDirectoryFile
{
    [JsonPropertyName("entries")]
    public List<PhoneDirectoryEntry> Entries { get; set; } = [];
}


/// <summary>
/// Contacto individual del directorio telefónico interno.
///
/// Cada entrada representa una persona, puesto o contacto interno
/// asociado a una categoría y a un centro.
/// </summary>
public sealed class PhoneDirectoryEntry
{
    /// <summary>
    /// Nombre principal que se mostrará al usuario.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Alias opcionales para mejorar la búsqueda.
    ///
    /// El JSON puede contener valores null y se ignorarán al buscar.
    /// </summary>
    [JsonPropertyName("aliases")]
    public List<string?> Aliases { get; set; } = [];

    /// <summary>
    /// Departamento o categoría organizativa.
    /// Ejemplos:
    /// Logistica, Compras, Ventas, Centro...
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    /// <summary>
    /// Centro al que pertenece el contacto.
    /// Ejemplos:
    /// Cash, Rodeo, Peru, Centro.
    /// </summary>
    [JsonPropertyName("center")]
    public string Center { get; set; } = "";

    /// <summary>
    /// Extensión telefónica interna opcional.
    /// </summary>
    [JsonPropertyName("extension")]
    public string? Extension { get; set; }

    /// <summary>
    /// Teléfono directo o móvil opcional.
    /// </summary>
    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; set; }
}

/// <summary>
/// Resultado de búsqueda dentro del directorio telefónico.
///
/// Incluye:
/// - El contacto encontrado.
/// - Una puntuación interna de relevancia,
///   usada para ordenar coincidencias.
/// </summary>
public sealed class PhoneDirectorySearchResult
{
    public PhoneDirectoryEntry Entry { get; set; } = new();

    public int Score { get; set; }
}