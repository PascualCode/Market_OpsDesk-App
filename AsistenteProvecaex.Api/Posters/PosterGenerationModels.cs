namespace Asistente.Api.Posters;

/// <summary>
/// Tamaños soportados para generación de carteles.
/// </summary>
public enum PosterSize
{
    A3,
    A4,
    A5,
    A6
}

/// <summary>
/// Referencia a los tipos de Cartel segun tipo de oferta
/// </summary>
public enum PosterPriceType
{
    Normal,
    Novedad,
    SuperOferta,
    Oferta1Mas1,
    Oferta2Mas1,
    Oferta3Mas1,
    Oferta4Mas1,
    Oferta5Mas1
}

/// <summary>
/// Define las formas permitidas de buscar productos
/// desde el generador de carteles.
///
/// Importante:
/// El usuario solo podrá buscar por un tipo cada vez:
/// - EAN / código de barras.
/// - Código interno de artículo.
/// - Fragmento del nombre o descripción.
/// </summary>
public enum PosterProductSearchType
{
    /// <summary>
    /// Búsqueda por código de barras / EAN.
    /// Ejemplo: 5060166690380.
    /// </summary>
    Ean,

    /// <summary>
    /// Búsqueda por código interno de artículo.
    /// Ejemplo: 02016033.
    /// </summary>
    ArticleCode,

    /// <summary>
    /// Búsqueda por fragmento del nombre o descripción.
    /// Ejemplo: "Mesa", "Monster", "Aceite".
    /// </summary>
    Name
}


/// <summary>
/// Convierte el texto recibido desde la API/Desktop
/// en un tipo de búsqueda válido.
///
/// Esto permite que el Desktop pueda enviar textos como:
/// - "EAN"
/// - "Código"
/// - "Código Artículo"
/// - "Nombre"
/// - "Descripción"
///
/// Si el valor recibido no se reconoce, se usa EAN por defecto.
/// </summary>
public static class PosterProductSearchTypeParser
{
    public static PosterProductSearchType ParseOrDefault(
        string? value,
        PosterProductSearchType defaultValue = PosterProductSearchType.Ean)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        var normalized = value
            .Trim()
            .ToUpperInvariant()
            .Replace(" ", "")
            .Replace("_", "")
            .Replace("-", "");

        return normalized switch
        {
            "EAN" => PosterProductSearchType.Ean,
            "CODIGO" => PosterProductSearchType.ArticleCode,
            "CODIGOARTICULO" => PosterProductSearchType.ArticleCode,
            "ARTICLECODE" => PosterProductSearchType.ArticleCode,
            "NOMBRE" => PosterProductSearchType.Name,
            "NAME" => PosterProductSearchType.Name,
            "DESCRIPCION" => PosterProductSearchType.Name,
            _ => defaultValue
        };
    }
}

public static class PosterSizeParser
{
    public static PosterSize ParseOrDefault(
        string? value,
        PosterSize defaultValue = PosterSize.A4)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return Enum.TryParse<PosterSize>(
            value,
            ignoreCase: true,
            out var parsed
        )
            ? parsed
            : defaultValue;
    }
}

public static class PosterPriceTypeParser
{
    public static PosterPriceType ParseOrDefault(
        string? value,
        PosterPriceType defaultValue = PosterPriceType.Normal)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        var normalized = value
            .Trim()
            .ToUpperInvariant()
            .Replace(" ", "")
            .Replace("+", "MAS")
            .Replace("Á", "A")
            .Replace("É", "E")
            .Replace("Í", "I")
            .Replace("Ó", "O")
            .Replace("Ú", "U");

        return normalized switch
        {
            "NORMAL" => PosterPriceType.Normal,
            "NOVEDAD" or "¡NOVEDAD!" => PosterPriceType.Novedad,
            "SUPEROFERTA" => PosterPriceType.SuperOferta,

            "OFERTA1MAS1" or "1MAS1" => PosterPriceType.Oferta1Mas1,
            "OFERTA2MAS1" or "2MAS1" => PosterPriceType.Oferta2Mas1,
            "OFERTA3MAS1" or "3MAS1" => PosterPriceType.Oferta3Mas1,
            "OFERTA4MAS1" or "4MAS1" => PosterPriceType.Oferta4Mas1,
            "OFERTA5MAS1" or "5MAS1" => PosterPriceType.Oferta5Mas1,

            _ => defaultValue
        };
    }

    /// <summary>
    /// Artículo individual dentro de una petición de generación mixta.
    /// </summary>
    public sealed class PosterBatchGenerationItemRequest
    {
        public string ProductCode { get; set; } = "";

        public string? Size { get; set; }

        public string? PriceType { get; set; }
    }

    /// <summary>
    /// Petición para generar un PDF con carteles de distintos tamaños
    /// y ofertas en un mismo documento.
    /// </summary>
    public sealed class PosterBatchGenerationRequest
    {
        public List<PosterBatchGenerationItemRequest> Items { get; set; } = [];
    }
}

/// <summary>
/// Elemento interno preparado para renderizar un cartel.
///
/// Este modelo ya contiene:
/// - producto completo recuperado desde SQL Server,
/// - tamaño del cartel,
/// - tipo de oferta.
/// </summary>
public sealed class PosterRenderItem
{
    public PosterProductDto Product { get; set; } = new();

    public PosterSize Size { get; set; } = PosterSize.A4;

    public PosterPriceType PriceType { get; set; } = PosterPriceType.Normal;
}