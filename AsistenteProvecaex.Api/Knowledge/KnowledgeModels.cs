/// <summary>
/// Representa un documento interno de conocimiento
/// cargado desde la carpeta knowledge.
/// </summary>
public sealed class KnowledgeDocument
{
    /// <summary>
    /// Ruta relativa dentro de la carpeta Knowledge.
    /// </summary>
    public string RelativePath { get; set; } = "";

    /// <summary>
    /// Nombre de la carpeta principal a la que pertenece.
    /// Por ejemplo:
    /// - redes
    /// - incidencias
    /// - facturacion
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// Título del documento.
    /// Se intenta extraer del primer encabezado Markdown "# Título".
    /// Si no existe, se usa el nombre del archivo.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Contenido completo del archivo.
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// Fecha de última modificación del documento.
    /// </summary>
    public DateTime LastModifiedUtc { get; set; }
}


/// <summary>
/// Petición para buscar documentos relevantes
/// dentro de la base de conocimiento.
/// </summary>
public sealed class KnowledgeSearchRequest
{
    /// <summary>
    /// Texto de búsqueda introducido por el usuario.
    /// </summary>
    public string Query { get; set; } = "";

    /// <summary>
    /// Número máximo de resultados a devolver.
    /// </summary>
    public int MaxResults { get; set; } = 3;
}


/// <summary>
/// Resultado individual de una búsqueda sobre
/// la base de conocimiento.
/// </summary>
public sealed class KnowledgeSearchResult
{
    /// <summary>
    /// Ruta relativa del documento dentro de knowledge.
    /// </summary>
    public string RelativePath { get; set; } = "";

    /// <summary>
    /// Categoría del documento.
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// Título del documento.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Puntuación calculada por el buscador.
    /// Cuanto mayor sea, más relevante se considera.
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// Pequeño fragmento de texto del documento
    /// para identificar su contenido.
    /// </summary>
    public string Preview { get; set; } = "";
}


/// <summary>
/// Representa el contexto de conocimiento que se añade
/// a la consulta enviada al modelo.
/// </summary>
public sealed class KnowledgePromptContext
{
    /// <summary>
    /// Texto final con la documentación interna seleccionada.
    /// </summary>
    public string ContextText { get; set; } = "";

    /// <summary>
    /// Documentos recuperados que se han utilizado
    /// para construir el contexto.
    /// </summary>
    public List<KnowledgeSearchResult> Results { get; set; } = [];

    /// <summary>
    /// Indica si realmente se ha generado contexto útil.
    /// </summary>
    public bool HasContext =>
        !string.IsNullOrWhiteSpace(ContextText);
}
