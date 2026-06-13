using System.Text.Json;

/// <summary>
/// Herramienta del sistema que devuelve la fecha y hora actuales
/// del servidor IA.
///
/// Es útil cuando el asistente necesita responder con una referencia
/// temporal real y no debe depender de una estimación del modelo.
/// </summary>
public sealed class GetServerDateTimeTool : IAssistantServerTool
{
    public string Name => "get_server_datetime";

    public OllamaToolDefinition Definition => new()
    {
        Type = "function",
        Function = new OllamaFunctionDefinition
        {
            Name = Name,
            Description =
                "Obtiene la fecha y hora actuales del servidor interno del asistente. " +
                "Debe usarse cuando el usuario pregunte por la fecha u hora actual del servidor.",
            Parameters = new OllamaFunctionParameters
            {
                Type = "object",
                Required = [],
                Properties = []
            }
        }
    };

    public Task<ToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        AssistantToolExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var now = DateTime.Now;

        var content =
            $"Fecha y hora actuales del servidor: {now:dd/MM/yyyy HH:mm:ss}.";

        return Task.FromResult(
            ToolExecutionResult.Ok(content)
        );
    }
}
