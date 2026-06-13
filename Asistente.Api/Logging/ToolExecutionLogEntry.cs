/// <summary>
/// Entrada de log generada cada vez que el motor de herramientas
/// intenta ejecutar una herramienta.
///
/// Sirve para registrar:
/// - Usuario propietario.
/// - Equipo origen.
/// - Herramienta solicitada.
/// - Si se ejecutó o no.
/// - Si terminó correctamente.
/// - Tiempo de ejecución.
/// - Argumentos y resultado resumido.
/// </summary>
public sealed class ToolExecutionLogEntry
{
    public DateTime Date { get; set; }

    public string OwnerKey { get; set; } = "";

    public string MachineName { get; set; } = "";

    public string ToolName { get; set; } = "";

    public bool Executed { get; set; }

    public bool Success { get; set; }

    public long ElapsedMs { get; set; }

    public string ArgumentsJson { get; set; } = "{}";

    public string Result { get; set; } = "";
}