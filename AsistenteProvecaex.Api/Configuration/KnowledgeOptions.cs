/// <summary>
/// Configuración de la base de conocimiento documental.
/// 
/// Controla:
/// - Si el sistema de conocimiento está activo.
/// - La carpeta donde se almacenan las guías.
/// - Las extensiones permitidas.
/// - Los límites del contexto documental que se envía al modelo.
/// </summary>
public sealed class KnowledgeOptions
{
    public bool Enabled { get; set; } = true;

    public string Directory { get; set; } =
        "/opt/asistente/knowledge";

    public List<string> AllowedExtensions { get; set; } =
        [".md", ".txt"];

    /// <summary>
    /// Número máximo de documentos internos que se insertarán
    /// como contexto mediante búsqueda por palabras clave.
    /// </summary>
    public int MaxDocumentsForPrompt { get; set; } = 3;

    /// <summary>
    /// Puntuación mínima para considerar que una guía es relevante
    /// en la búsqueda tradicional por palabras clave.
    /// </summary>
    public int MinScoreForPrompt { get; set; } = 2;

    /// <summary>
    /// Límite de caracteres de documentación interna que se incorporan
    /// al prompt cuando se usa la recuperación por palabras clave.
    /// </summary>
    public int MaxContextCharacters { get; set; } = 8000;
}
