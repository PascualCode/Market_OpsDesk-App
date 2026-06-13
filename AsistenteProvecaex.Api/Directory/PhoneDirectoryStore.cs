using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text;

/// <summary>
/// Servicio encargado de cargar y mantener en memoria
/// el directorio telefónico interno.
///
/// El origen de datos es un fichero JSON editable:
/// /opt/asistente/directory/phone-directory.json
///
/// Permite:
/// - Cargar el directorio al arrancar la API.
/// - Recargarlo manualmente más adelante.
/// - Devolver una copia segura de sus entradas.
/// </summary>
public sealed class PhoneDirectoryStore
{
    private readonly PhoneDirectoryOptions _options;

    /// <summary>
    /// Bloqueo simple para evitar lecturas y recargas
    /// simultáneas sobre la lista de contactos.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// Entradas cargadas actualmente en memoria.
    /// </summary>
    private List<PhoneDirectoryEntry> _entries = [];

    /// <summary>
    /// Indica si el módulo está habilitado.
    /// </summary>
    public bool Enabled => _options.Enabled;

    /// <summary>
    /// Ruta del fichero JSON del directorio.
    /// </summary>
    public string FilePath => _options.FilePath;

    public PhoneDirectoryStore(IConfiguration configuration)
    {
        _options = configuration
            .GetSection("PhoneDirectory")
            .Get<PhoneDirectoryOptions>() ?? new PhoneDirectoryOptions();

        Reload();
    }

    /// <summary>
    /// Devuelve una copia segura de las entradas cargadas.
    /// </summary>
    public List<PhoneDirectoryEntry> GetEntries()
    {
        lock (_lock)
        {
            return _entries.ToList();
        }
    }

    /// <summary>
    /// Busca contactos dentro del directorio telefónico.
    ///
    /// Permite filtrar por:
    /// - Texto libre: nombre, alias, extensión o teléfono.
    /// - Categoría.
    /// - Centro.
    ///
    /// Los resultados se ordenan por relevancia.
    /// </summary>
    public List<PhoneDirectorySearchResult> Search(
        string? query,
        string? category = null,
        string? center = null,
        int maxResults = 10)
    {
        var entries = GetEntries();

        if (entries.Count == 0)
            return [];

        maxResults = Math.Clamp(maxResults, 1, 25);

        var normalizedQuery = NormalizeText(query);
        var normalizedCategory = NormalizeText(category);
        var normalizedCenter = NormalizeText(center);

        var queryTerms = normalizedQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var results = new List<PhoneDirectorySearchResult>();

        foreach (var entry in entries)
        {
            if (!MatchesFilter(entry.Category, normalizedCategory))
                continue;

            if (!MatchesFilter(entry.Center, normalizedCenter))
                continue;

            var score = CalculateScore(
                entry,
                normalizedQuery,
                queryTerms,
                normalizedCategory,
                normalizedCenter
            );

            // Si no hay texto de búsqueda, pero sí filtros,
            // devolvemos coincidencias filtradas.
            var hasOnlyFilters =
                string.IsNullOrWhiteSpace(normalizedQuery) &&
                (!string.IsNullOrWhiteSpace(normalizedCategory) ||
                 !string.IsNullOrWhiteSpace(normalizedCenter));

            if (score <= 0 && !hasOnlyFilters)
                continue;

            if (hasOnlyFilters && score <= 0)
                score = 1;

            results.Add(new PhoneDirectorySearchResult
            {
                Entry = entry,
                Score = score
            });
        }

        return results
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Entry.Name)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Comprueba si un valor encaja con un filtro opcional.
    /// 
    /// Si el filtro está vacío, siempre devuelve true.
    /// </summary>
    private static bool MatchesFilter(
        string value,
        string normalizedFilter)
    {
        if (string.IsNullOrWhiteSpace(normalizedFilter))
            return true;

        var normalizedValue = NormalizeText(value);

        return normalizedValue == normalizedFilter ||
               normalizedValue.Contains(normalizedFilter);
    }

    /// <summary>
    /// Calcula la relevancia de una entrada para una búsqueda.
    ///
    /// Priorizamos:
    /// - Coincidencias exactas de nombre.
    /// - Coincidencias por alias.
    /// - Extensión o teléfono exactos.
    /// - Coincidencias parciales por términos.
    /// - Categoría y centro cuando se reciben como filtros.
    /// </summary>
    private static int CalculateScore(
        PhoneDirectoryEntry entry,
        string normalizedQuery,
        List<string> queryTerms,
        string normalizedCategory,
        string normalizedCenter)
    {
        var score = 0;

        var normalizedName = NormalizeText(entry.Name);

        var normalizedAliases = entry.Aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(alias => NormalizeText(alias))
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .ToList();

        var normalizedExtension = NormalizeText(entry.Extension);
        var normalizedPhone = NormalizeText(entry.PhoneNumber);
        var entryCategory = NormalizeText(entry.Category);
        var entryCenter = NormalizeText(entry.Center);

        // ---------------------------------------------------------
        // Coincidencias exactas fuertes
        // ---------------------------------------------------------
        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            if (normalizedName == normalizedQuery)
                score += 120;

            if (normalizedAliases.Any(alias => alias == normalizedQuery))
                score += 110;

            if (!string.IsNullOrWhiteSpace(normalizedExtension) &&
                normalizedExtension == normalizedQuery)
            {
                score += 140;
            }

            if (!string.IsNullOrWhiteSpace(normalizedPhone) &&
                normalizedPhone == normalizedQuery)
            {
                score += 140;
            }
        }

        // ---------------------------------------------------------
        // Coincidencias parciales de frase
        // ---------------------------------------------------------
        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            if (normalizedName.Contains(normalizedQuery))
                score += 80;

            if (normalizedAliases.Any(alias => alias.Contains(normalizedQuery)))
                score += 70;

            if (!string.IsNullOrWhiteSpace(normalizedExtension) &&
                normalizedExtension.Contains(normalizedQuery))
            {
                score += 90;
            }

            if (!string.IsNullOrWhiteSpace(normalizedPhone) &&
                normalizedPhone.Contains(normalizedQuery))
            {
                score += 90;
            }
        }

        // ---------------------------------------------------------
        // Coincidencias por términos sueltos
        // Ejemplo:
        // "ivan muelle" debe encontrar "IVAN ACEVEDO MUELLE"
        // ---------------------------------------------------------
        foreach (var term in queryTerms)
        {
            if (normalizedName.Contains(term))
                score += 18;

            if (normalizedAliases.Any(alias => alias.Contains(term)))
                score += 15;

            if (entryCategory.Contains(term))
                score += 8;

            if (entryCenter.Contains(term))
                score += 8;

            if (!string.IsNullOrWhiteSpace(normalizedExtension) &&
                normalizedExtension.Contains(term))
            {
                score += 20;
            }

            if (!string.IsNullOrWhiteSpace(normalizedPhone) &&
                normalizedPhone.Contains(term))
            {
                score += 20;
            }
        }

        // ---------------------------------------------------------
        // Refuerzo por filtros explícitos
        // ---------------------------------------------------------
        if (!string.IsNullOrWhiteSpace(normalizedCategory) &&
            entryCategory == normalizedCategory)
        {
            score += 20;
        }

        if (!string.IsNullOrWhiteSpace(normalizedCenter) &&
            entryCenter == normalizedCenter)
        {
            score += 20;
        }

        return score;
    }

    /// <summary>
    /// Normaliza texto para búsquedas tolerantes:
    /// - Minúsculas.
    /// - Sin tildes.
    /// - Sin puntuación relevante.
    /// - Espacios compactados.
    /// </summary>
    private static string NormalizeText(string? text)
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
            var category =
                CharUnicodeInfo.GetUnicodeCategory(character);

            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
            {
                sb.Append(character);
            }
            else
            {
                sb.Append(' ');
            }
        }

        var normalized = sb
            .ToString()
            .Normalize(NormalizationForm.FormC);

        return string.Join(
            ' ',
            normalized.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries
            )
        );
    }

    /// <summary>
    /// Recarga el directorio telefónico desde el JSON configurado.
    ///
    /// Si:
    /// - El módulo está deshabilitado,
    /// - El fichero no existe,
    /// - O el contenido no puede deserializarse,
    ///
    /// la lista en memoria queda vacía.
    /// </summary>
    public void Reload()
    {
        lock (_lock)
        {
            _entries = [];

            if (!_options.Enabled)
                return;

            if (!File.Exists(_options.FilePath))
                return;

            try
            {
                var json = File.ReadAllText(_options.FilePath);

                var file =
                    JsonSerializer.Deserialize<PhoneDirectoryFile>(
                        json,
                        JsonOptions.Default
                    );

                if (file?.Entries is null)
                    return;

                _entries = file.Entries
                    .Where(entry =>
                        !string.IsNullOrWhiteSpace(entry.Name))
                    .Select(NormalizeEntry)
                    .ToList();
            }
            catch
            {
                // Si el JSON no puede leerse o está mal formado,
                // dejamos el directorio vacío.
                // Más adelante, si queremos, podemos añadir logging específico.
                _entries = [];
            }
        }
    }

    /// <summary>
    /// Normaliza valores básicos de una entrada:
    /// - Evita listas de alias nulas.
    /// - Recorta espacios innecesarios.
    /// - Conserva null cuando extensión o teléfono no existen.
    /// </summary>
    private static PhoneDirectoryEntry NormalizeEntry(
        PhoneDirectoryEntry entry)
    {
        return new PhoneDirectoryEntry
        {
            Name = entry.Name?.Trim() ?? "",

            Aliases = entry.Aliases?
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Select(alias => alias!.Trim())
                .ToList() ?? [],

            Category = entry.Category?.Trim() ?? "",

            Center = entry.Center?.Trim() ?? "",

            Extension = string.IsNullOrWhiteSpace(entry.Extension)
                ? null
                : entry.Extension.Trim(),

            PhoneNumber = string.IsNullOrWhiteSpace(entry.PhoneNumber)
                ? null
                : entry.PhoneNumber.Trim()
        };
    }
}