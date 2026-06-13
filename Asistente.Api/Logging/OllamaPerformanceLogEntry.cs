/// <summary>
/// Entrada de log de rendimiento devuelta por Ollama.
///
/// Permite medir con precisión:
/// - Tiempo total de la operación.
/// - Carga del modelo.
/// - Tokens del prompt.
/// - Tiempo de evaluación del prompt.
/// - Tokens generados.
/// - Tiempo de generación.
///
/// Las duraciones se guardan ya convertidas a milisegundos
/// para que el log sea más fácil de leer.
/// </summary>
public sealed class OllamaPerformanceLogEntry
{
    public DateTime Date { get; set; }

    /// <summary>
    /// Fase interna de la petición.
    /// Ejemplos:
    /// - tool_decision
    /// - final_answer
    /// - chat_without_tools
    /// </summary>
    public string Stage { get; set; } = "";

    public string Model { get; set; } = "";

    public long? TotalMs { get; set; }

    public long? LoadMs { get; set; }

    public int? PromptTokens { get; set; }

    public long? PromptEvalMs { get; set; }

    public int? OutputTokens { get; set; }

    public long? EvalMs { get; set; }
}