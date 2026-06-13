/// <summary>
/// Configuración de la búsqueda semántica RAG.
/// 
/// Controla:
/// - Si la búsqueda vectorial está habilitada.
/// - Cuántos fragmentos se recuperan.
/// - El umbral mínimo de similitud.
/// - El tamaño máximo del contexto enviado al modelo.
/// - Si debe usarse fallback por palabras clave.
/// </summary>
public sealed class RagSearchOptions
{
    /// <summary>
    /// Activa o desactiva la recuperación semántica desde Qdrant.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Número máximo de fragmentos semánticos recuperados.
    /// </summary>
    public int TopK { get; set; } = 4;

    /// <summary>
    /// Umbral mínimo de similitud para aceptar un fragmento.
    /// </summary>
    public double ScoreThreshold { get; set; } = 0.45;

    /// <summary>
    /// Máximo de caracteres del contexto semántico
    /// que se enviará al modelo.
    /// </summary>
    public int MaxContextCharacters { get; set; } = 6000;

    /// <summary>
    /// Si la búsqueda semántica no devuelve resultados útiles,
    /// permite usar la búsqueda tradicional por palabras clave.
    /// </summary>
    public bool UseKeywordFallback { get; set; } = true;
}
