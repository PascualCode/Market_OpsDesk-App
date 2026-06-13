using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Servicio encargado de cargar, mantener en memoria
/// y consultar los documentos de conocimiento interno.
///
/// En esta versión:
/// - Lee archivos .md y .txt.
/// - Los mantiene en memoria.
/// - Permite recargarlos sin reiniciar la API.
/// - Ejecuta búsqueda básica por palabras clave.
///
/// Más adelante convive con el sistema RAG semántico.
/// </summary>
public sealed class KnowledgeStore
{
    private readonly KnowledgeOptions _options;

    /// <summary>
    /// Bloqueo simple para proteger la lista de documentos
    /// durante recargas o lecturas concurrentes.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// Documentos cargados actualmente en memoria.
    /// </summary>
    private List<KnowledgeDocument> _documents = [];

    /// <summary>
    /// Indica si el sistema de conocimiento está habilitado.
    /// </summary>
    public bool Enabled => _options.Enabled;

    /// <summary>
    /// Carpeta raíz desde la que se cargan las guías.
    /// </summary>
    public string Directory => _options.Directory;

    public KnowledgeStore(IConfiguration configuration)
    {
        _options = configuration
            .GetSection("Knowledge")
            .Get<KnowledgeOptions>() ?? new KnowledgeOptions();

        Reload();
    }

    /// <summary>
    /// Devuelve una copia segura de los documentos
    /// cargados actualmente en memoria.
    /// </summary>
    public List<KnowledgeDocument> GetDocuments()
    {
        lock (_lock)
        {
            return _documents.ToList();
        }
    }

    /// <summary>
    /// Recarga todos los documentos desde disco.
    ///
    /// Se usa:
    /// - Al arrancar la API.
    /// - Cuando se llama a /api/knowledge/reload.
    /// </summary>
    public void Reload()
    {
        lock (_lock)
        {
            _documents = [];

            if (!_options.Enabled)
                return;

            if (!System.IO.Directory.Exists(_options.Directory))
                return;

            var allowedExtensions = _options.AllowedExtensions
                .Select(extension => extension.ToLowerInvariant())
                .ToHashSet();

            var files = System.IO.Directory
                .EnumerateFiles(
                    _options.Directory,
                    "*.*",
                    SearchOption.AllDirectories
                )
                .Where(file =>
                    allowedExtensions.Contains(
                        Path.GetExtension(file).ToLowerInvariant()
                    )
                )
                .OrderBy(file => file)
                .ToList();

            foreach (var file in files)
            {
                try
                {
                    var content = File.ReadAllText(
                        file,
                        Encoding.UTF8
                    );

                    var relativePath = Path
                        .GetRelativePath(_options.Directory, file)
                        .Replace("\\", "/");

                    var category = ExtractCategory(relativePath);
                    var title = ExtractTitle(content, file);

                    _documents.Add(new KnowledgeDocument
                    {
                        RelativePath = relativePath,
                        Category = category,
                        Title = title,
                        Content = content,
                        LastModifiedUtc = File.GetLastWriteTimeUtc(file)
                    });
                }
                catch
                {
                    // Si un archivo no puede leerse, se omite.
                    // Más adelante podemos registrar este fallo en logs.
                }
            }
        }
    }

    /// <summary>
    /// Obtiene la categoría a partir del primer segmento
    /// de la ruta relativa del documento.
    ///
    /// Ejemplo:
    /// redes/comprobar_conexion_windows.md
    /// → redes
    /// </summary>
    private static string ExtractCategory(string relativePath)
    {
        var normalized = relativePath.Replace("\\", "/");

        var parts = normalized.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries
        );

        return parts.Length > 1
            ? parts[0]
            : "general";
    }

    /// <summary>
    /// Intenta extraer el título del documento desde
    /// el primer encabezado Markdown "# Título".
    ///
    /// Si no existe, usa el nombre del archivo.
    /// </summary>
    private static string ExtractTitle(
        string content,
        string filePath)
    {
        var lines = content.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("# "))
            {
                return trimmed[2..].Trim();
            }
        }

        return Path.GetFileNameWithoutExtension(filePath);
    }

    /// <summary>
    /// Busca documentos relevantes según una consulta de texto.
    ///
    /// Esta búsqueda tradicional:
    /// - Normaliza el texto.
    /// - Extrae palabras relevantes.
    /// - Puntúa cada documento.
    /// - Devuelve los mejores resultados ordenados.
    ///
    /// Aunque el sistema principal usa RAG semántico,
    /// esta búsqueda se mantiene como fallback y diagnóstico.
    /// </summary>
    public List<KnowledgeSearchResult> Search(
        string query,
        int maxResults = 3)
    {
        var documents = GetDocuments();

        if (documents.Count == 0)
            return [];

        var queryTerms = ExtractSearchTerms(query);

        if (queryTerms.Count == 0)
            return [];

        var results = new List<KnowledgeSearchResult>();

        foreach (var document in documents)
        {
            var score = CalculateDocumentScore(
                document,
                queryTerms
            );

            if (score <= 0)
                continue;

            results.Add(new KnowledgeSearchResult
            {
                RelativePath = document.RelativePath,
                Category = document.Category,
                Title = document.Title,
                Score = score,
                Preview = BuildPreview(
                    document.Content,
                    queryTerms
                )
            });
        }

        return results
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Title)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Extrae términos relevantes de una consulta.
    ///
    /// Se normaliza:
    /// - Minúsculas.
    /// - Sin tildes.
    /// - Sin signos de puntuación.
    ///
    /// Además, se eliminan palabras comunes que
    /// aportan poco a la búsqueda.
    /// </summary>
    private static List<string> ExtractSearchTerms(string text)
    {
        var normalized = NormalizeText(text);

        var rawTerms = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 3)
            .ToList();

        var stopWords = new HashSet<string>
        {
            "que", "para", "con", "por", "una", "uno", "unos", "unas",
            "del", "las", "los", "como", "esto", "esta", "este", "tengo",
            "tiene", "hacer", "puedo", "quiero", "necesito", "problema",
            "error", "algo", "desde", "donde", "cuando", "porque", "sobre",
            "equipo", "usuario", "revisar", "comprobar"
        };

        return rawTerms
            .Where(term => !stopWords.Contains(term))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Calcula la relevancia de un documento frente a los términos buscados.
    ///
    /// Pesos utilizados:
    /// - Coincidencia en título: +8
    /// - Coincidencia en categoría: +5
    /// - Coincidencia en ruta: +4
    /// - Coincidencia en contenido: +1 por aparición, con límite
    /// </summary>
    private static int CalculateDocumentScore(
        KnowledgeDocument document,
        List<string> queryTerms)
    {
        var normalizedTitle = NormalizeText(document.Title);
        var normalizedCategory = NormalizeText(document.Category);
        var normalizedPath = NormalizeText(document.RelativePath);
        var normalizedContent = NormalizeText(document.Content);

        var score = 0;

        foreach (var term in queryTerms)
        {
            if (normalizedTitle.Contains(term))
                score += 8;

            if (normalizedCategory.Contains(term))
                score += 5;

            if (normalizedPath.Contains(term))
                score += 4;

            var contentOccurrences = CountOccurrences(
                normalizedContent,
                term
            );

            // Limitamos el peso del contenido para evitar
            // que una palabra repetida muchas veces distorsione el ranking.
            score += Math.Min(contentOccurrences, 5);
        }

        return score;
    }

    /// <summary>
    /// Cuenta cuántas veces aparece un término dentro de un texto.
    /// </summary>
    private static int CountOccurrences(
        string text,
        string term)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            string.IsNullOrWhiteSpace(term))
        {
            return 0;
        }

        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(
                   term,
                   index,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += term.Length;
        }

        return count;
    }

    /// <summary>
    /// Genera una vista previa corta del documento.
    ///
    /// Intenta mostrar un fragmento cercano
    /// a la primera coincidencia relevante.
    /// Si no encuentra coincidencia,
    /// devuelve el inicio del documento.
    /// </summary>
    private static string BuildPreview(
        string content,
        List<string> queryTerms)
    {
        const int previewLength = 280;

        if (string.IsNullOrWhiteSpace(content))
            return "";

        var normalizedContent = NormalizeText(content);

        var firstMatchIndex = -1;

        foreach (var term in queryTerms)
        {
            var index = normalizedContent.IndexOf(
                term,
                StringComparison.Ordinal
            );

            if (index >= 0 &&
                (firstMatchIndex == -1 || index < firstMatchIndex))
            {
                firstMatchIndex = index;
            }
        }

        if (firstMatchIndex == -1)
        {
            return CleanPreview(
                content[..Math.Min(content.Length, previewLength)]
            );
        }

        var start = Math.Max(0, firstMatchIndex - 80);
        var length = Math.Min(
            previewLength,
            content.Length - start
        );

        var preview = content.Substring(start, length);

        return CleanPreview(preview);
    }

    /// <summary>
    /// Limpia saltos de línea y espacios excesivos
    /// para devolver una vista previa más legible.
    /// </summary>
    private static string CleanPreview(string text)
    {
        return text
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("  ", " ")
            .Trim();
    }

    /// <summary>
    /// Normaliza texto para facilitar las búsquedas:
    /// - Convierte a minúsculas.
    /// - Elimina tildes.
    /// - Sustituye signos por espacios.
    /// </summary>
    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var lower = text.ToLowerInvariant();

        var decomposed = lower.Normalize(
            NormalizationForm.FormD
        );

        var sb = new StringBuilder();

        foreach (var character in decomposed)
        {
            var unicodeCategory =
                CharUnicodeInfo.GetUnicodeCategory(character);

            if (unicodeCategory ==
                UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                sb.Append(character);
            }
            else
            {
                sb.Append(' ');
            }
        }

        return sb
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }
}