using System.Text;
using System.Text.Json;

/// <summary>
/// Herramienta que permite buscar contactos
/// en el directorio telefónico interno del supermercado
///
/// Puede buscar por:
/// - Nombre.
/// - Alias.
/// - Categoría/departamento.
/// - Centro.
/// - Extensión.
/// - Número de teléfono.
/// </summary>
public sealed class SearchPhoneDirectoryTool : IAssistantServerTool
{
    private readonly PhoneDirectoryStore _directoryStore;

    public SearchPhoneDirectoryTool(
        PhoneDirectoryStore directoryStore)
    {
        _directoryStore = directoryStore;
    }

    public string Name => "search_phone_directory";

    public OllamaToolDefinition Definition => new()
    {
        Type = "function",
        Function = new OllamaFunctionDefinition
        {
            Name = Name,
            Description =
                "Busca contactos en el directorio telefónico interno. " +
                "Úsala cuando el usuario pregunte por teléfonos, extensiones, " +
                "números de contacto, personas de un departamento o contactos de un centro. " +
                "Permite buscar por nombre, categoría, centro, extensión o teléfono.",
            Parameters = new OllamaFunctionParameters
            {
                Type = "object",
                Required = [],
                Properties = new()
                {
                    ["query"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "Texto libre de búsqueda. Puede ser un nombre, alias, extensión o teléfono."
                    },
                    ["category"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "Categoría o departamento opcional. Ejemplos: Logistica, Ventas, Compras, Informatica, Comerciales, Repartidores, Almacen, Mantenimiento, Mostradores o Centro."
                    },
                    ["center"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "Centro opcional. Valores habituales: Cash, Rodeo, Peru o Centro."
                    },
                    ["maxResults"] = new OllamaFunctionParameterProperty
                    {
                        Type = "integer",
                        Description =
                            "Número máximo de resultados que se desean recuperar. Si no se indica, se usarán 10."
                    }
                }
            }
        }
    };

    public Task<ToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        AssistantToolExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        if (!_directoryStore.Enabled)
        {
            return Task.FromResult(
                ToolExecutionResult.Fail(
                    "El directorio telefónico interno está deshabilitado."
                )
            );
        }

        var query = ToolArgumentReader.GetOptionalString(
            arguments,
            "query"
        );

        var category = ToolArgumentReader.GetOptionalString(
            arguments,
            "category"
        );

        var center = ToolArgumentReader.GetOptionalString(
            arguments,
            "center"
        );

        var maxResults = GetOptionalInt(
            arguments,
            "maxResults",
            defaultValue: 10
        );

        if (string.IsNullOrWhiteSpace(query) &&
            string.IsNullOrWhiteSpace(category) &&
            string.IsNullOrWhiteSpace(center))
        {
            return Task.FromResult(
                ToolExecutionResult.Fail(
                    "No se ha indicado ningún criterio válido para buscar en el directorio telefónico."
                )
            );
        }

        var results = _directoryStore.Search(
            query,
            category,
            center,
            maxResults
        );

        if (results.Count == 0)
        {
            return Task.FromResult(
                ToolExecutionResult.Ok(
                    "No se ha encontrado ningún contacto que coincida con la búsqueda."
                )
            );
        }

        var sb = new StringBuilder();

        sb.AppendLine(
            results.Count == 1
                ? "Contacto encontrado:"
                : $"Contactos encontrados: {results.Count}"
        );

        foreach (var result in results)
        {
            var entry = result.Entry;

            sb.AppendLine(
                $"- {entry.Name} | " +
                $"Categoría: {entry.Category} | " +
                $"Centro: {entry.Center} | " +
                $"Extensión: {FormatOptionalValue(entry.Extension)} | " +
                $"Teléfono: {FormatOptionalValue(entry.PhoneNumber)}"
            );
        }

        return Task.FromResult(
            ToolExecutionResult.Ok(
                sb.ToString().Trim()
            )
        );
    }

    /// <summary>
    /// Lee un entero opcional de los argumentos JSON.
    /// Si no existe o no es válido, devuelve el valor por defecto.
    /// </summary>
    private static int GetOptionalInt(
        JsonElement arguments,
        string propertyName,
        int defaultValue)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            return defaultValue;

        if (!arguments.TryGetProperty(propertyName, out var property))
            return defaultValue;

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out var intValue))
        {
            return Math.Clamp(intValue, 1, 25);
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), out var parsedValue))
        {
            return Math.Clamp(parsedValue, 1, 25);
        }

        return defaultValue;
    }

    /// <summary>
    /// Muestra valores opcionales de forma clara
    /// en el resultado que recibe el modelo.
    /// </summary>
    private static string FormatOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "No consta"
            : value;
    }
}