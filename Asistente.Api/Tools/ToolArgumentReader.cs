using System.Text.Json;

/// <summary>
/// Utilidad auxiliar para leer argumentos recibidos desde
/// las llamadas a herramientas del modelo.
/// </summary>
public static class ToolArgumentReader
{
    /// <summary>
    /// Obtiene una propiedad de tipo texto si existe.
    /// 
    /// Si la propiedad no existe o viene vacía,
    /// devuelve null.
    /// </summary>
    public static string? GetOptionalString(
        JsonElement arguments,
        string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            return null;

        if (!arguments.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }
}