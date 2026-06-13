using System.Text.Json;

/// <summary>
/// Contrato base que debe cumplir cualquier herramienta
/// ejecutable en el servidor del asistente.
///
/// Ejemplos:
/// - create_task
/// - list_tasks
/// - create_reminder
/// - get_server_datetime
/// </summary>
public interface IAssistantServerTool
{
    /// <summary>
    /// Nombre técnico de la herramienta.
    /// Debe coincidir con el nombre que recibe Ollama.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Definición estructurada de la herramienta
    /// que se envía al modelo.
    /// </summary>
    OllamaToolDefinition Definition { get; }

    /// <summary>
    /// Ejecuta la herramienta con:
    /// - Argumentos generados por el modelo.
    /// - Contexto del usuario/equipo.
    /// - Token de cancelación.
    /// </summary>
    Task<ToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        AssistantToolExecutionContext executionContext,
        CancellationToken cancellationToken);
}