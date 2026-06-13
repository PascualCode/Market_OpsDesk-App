/// <summary>
/// Resultado estándar de la ejecución de una herramienta.
///
/// Permite devolver:
/// - Éxito o fallo.
/// - Mensaje textual que después podrá usar el modelo
///   para construir la respuesta final al usuario.
/// </summary>
public sealed class ToolExecutionResult
{
    /// <summary>
    /// Indica si la herramienta se ejecutó correctamente.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Resultado textual de la herramienta.
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// Crea un resultado exitoso.
    /// </summary>
    public static ToolExecutionResult Ok(string content)
    {
        return new ToolExecutionResult
        {
            Success = true,
            Content = content
        };
    }

    /// <summary>
    /// Crea un resultado fallido.
    /// </summary>
    public static ToolExecutionResult Fail(string content)
    {
        return new ToolExecutionResult
        {
            Success = false,
            Content = content
        };
    }
}