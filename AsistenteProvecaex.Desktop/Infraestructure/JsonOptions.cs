using System.Text.Json;
using System.Text.Json.Serialization;

namespace Asistente.Desktop.Infrastructure;

/// <summary>
/// Opciones JSON compartidas por el Desktop.
///
/// PropertyNameCaseInsensitive permite leer JSON aunque cambie
/// mayúsculas/minúsculas.
///
/// WhenWritingNull evita enviar propiedades null, como Model cuando
/// queremos que lo decida la API.
/// </summary>
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}