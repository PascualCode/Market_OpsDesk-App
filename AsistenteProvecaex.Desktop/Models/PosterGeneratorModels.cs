namespace Asistente.Desktop.Models;

/// <summary>
/// Resultado de generar un cartel PDF desde la API.
/// </summary>
public sealed class PosterGenerationResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public QuickPrintSelectedFile? GeneratedPdf { get; set; }
}

/// <summary>
/// Producto devuelto por la API para el generador de carteles.
/// </summary>
public sealed class PosterProductItem
{
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Brand { get; set; }

    public string? Format { get; set; }

    public decimal? Price { get; set; }

    public string? VatCode { get; set; }

    public string? Unit { get; set; }

    public string? Ean { get; set; }

    /// <summary>
    /// Texto compacto para mostrar en listas del Desktop.
    /// </summary>
    public string DisplayText
    {
        get
        {
            var priceText = Price.HasValue
                ? $"{Price.Value:0.00} €"
                : "Sin precio";

            return $"{Code} - {Name} - {priceText}";
        }
    }
}

/// <summary>
/// Respuesta de búsqueda de productos devuelta por la API.
/// </summary>
public sealed class PosterProductSearchResponse
{
    public bool Found { get; set; }

    public int Count { get; set; }

    public List<PosterProductItem> Products { get; set; } = [];
}

/// <summary>
/// Artículo enviado a la API para generar un cartel.
///
/// Cada artículo lleva su propio tamaño y tipo de oferta.
/// </summary>
public sealed class PosterBatchGenerationItemRequest
{
    public string ProductCode { get; set; } = "";

    public string Size { get; set; } = "A4";

    public string PriceType { get; set; } = "Normal";
}

/// <summary>
/// Petición enviada desde Desktop a la API
/// para generar un PDF mixto de carteles.
/// </summary>
public sealed class PosterBatchGenerationRequest
{
    public List<PosterBatchGenerationItemRequest> Items { get; set; } = [];
}

/// <summary>
/// Artículo añadido al lote de generación de carteles.
///
/// A diferencia de PosterProductItem, este modelo representa
/// una instancia concreta dentro del lote.
///
/// Esto permite:
/// - Añadir el mismo producto varias veces.
/// - Asignar tamaño propio a cada artículo.
/// - Asignar oferta propia a cada artículo.
/// </summary>
public sealed class PosterSelectedProductItem
{
    public Guid InstanceId { get; set; } = Guid.NewGuid();

    public PosterProductItem Product { get; set; } = new();

    public string Size { get; set; } = "A4";

    public string PriceType { get; set; } = "Normal";

    public string ProductCode => Product.Code;

    public string DisplayText
    {
        get
        {
            var priceText = Product.Price.HasValue
                ? $"{Product.Price.Value:0.00} €"
                : "Sin precio";

            return $"{Product.Code} - {Product.Name} - {priceText} - {Size} - {PriceType}";
        }
    }
}

/// <summary>
/// Etiqueta pendiente de impresión recibida desde la API.
/// </summary>
public sealed class PendingLabelItem
{
    public long ReturnRowId { get; set; }

    public string GroupId { get; set; } = "";

    public int Printed { get; set; }

    public string Warehouse { get; set; } = "";

    public string? Location { get; set; }

    public string ArticleCode { get; set; } = "";

    public string ArticleDescription { get; set; } = "";

    public string RateCode { get; set; } = "";

    public decimal? OldPrice { get; set; }

    public decimal? NewPrice { get; set; }

    public decimal Price { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? Barcode { get; set; }

    public string? BarcodeAux { get; set; }

    public string? SupplierCode { get; set; }

    public string? SupplierName { get; set; }

    public string? Key { get; set; }

    public string? VatCode { get; set; }

    public string? ManipulationUnit { get; set; }

    public decimal? Factor { get; set; }

    public string? UnitDescription { get; set; }

    public DateTime? PrintedAt { get; set; }

    public string? User { get; set; }

    public string EffectiveBarcode { get; set; } = "";

    /// <summary>
    /// Texto visible en las listas del Desktop.
    /// </summary>
    public string DisplayText
    {
        get
        {
            return $"{ReturnRowId} - {ArticleCode} - {ArticleDescription} - {Price:0.00} € - IVA {VatCode}";
        }
    }
}

/// <summary>
/// Respuesta de la API con etiquetas pendientes.
/// </summary>
public sealed class PendingLabelsResponse
{
    public bool Found { get; set; }

    public int Count { get; set; }

    public List<PendingLabelItem> Labels { get; set; } = [];
}

/// <summary>
/// Petición para generar PDF de etiquetas desde Desktop.
/// </summary>
public sealed class LabelBatchGenerationRequest
{
    public List<long> RowIds { get; set; } = [];

    public string Format { get; set; } = "Normal";
}

public sealed class MarkLabelsAsPrintedRequest
{
    public List<long> RowIds { get; set; } = [];

    public string? User { get; set; }
}

public sealed class MarkLabelsAsPrintedResult
{
    public bool Success { get; set; }

    public int RequestedCount { get; set; }

    public int UpdatedCount { get; set; }

    public string? Message { get; set; }
}