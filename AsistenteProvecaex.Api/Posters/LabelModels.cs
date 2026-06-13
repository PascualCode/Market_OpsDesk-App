namespace Asistente.Api.Posters;

/// <summary>
/// Etiqueta pendiente de impresión obtenida desde
/// Vista_Etiquetas_PreciosModi.
///
/// Este modelo representa una línea concreta de etiqueta.
/// El identificador principal será ReturnRowId.
/// </summary>
public sealed class PendingLabelDto
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

    /// <summary>
    /// Código de barras que se usará para pintar la etiqueta.
    /// Preferimos Return_CodBarraAux y, si viene vacío, usamos CodBarra.
    /// </summary>
    public string EffectiveBarcode
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(BarcodeAux))
                return BarcodeAux;

            return Barcode ?? "";
        }
    }

    /// <summary>
    /// Texto compacto para mostrar en Desktop.
    /// </summary>
    public string DisplayText
    {
        get
        {
            return $"{ArticleCode} - {ArticleDescription} - {Price:0.00} € - IVA {VatCode}";
        }
    }
}

/// <summary>
/// Respuesta del endpoint de etiquetas pendientes.
/// </summary>
public sealed class PendingLabelsResponse
{
    public bool Found { get; set; }

    public int Count { get; set; }

    public List<PendingLabelDto> Labels { get; set; } = [];
}

/// <summary>
/// Petición para generar PDF de etiquetas seleccionadas.
/// </summary>
public sealed class LabelBatchGenerationRequest
{
    /// <summary>
    /// Identificadores Return_Row_Id de las etiquetas seleccionadas.
    /// </summary>
    public List<long> RowIds { get; set; } = [];

    /// <summary>
    /// Formato visual de etiqueta:
    /// Normal, Reducido o SuperReducido.
    /// </summary>
    public string? Format { get; set; }
}

/// <summary>
/// Formatos disponibles para impresión de etiquetas.
/// </summary>
public enum LabelFormat
{
    Normal,
    Reducido,
    SuperReducido
}

/// <summary>
/// Parser para convertir texto recibido desde Desktop/API
/// en formato de etiqueta.
/// </summary>
public static class LabelFormatParser
{
    public static LabelFormat ParseOrDefault(
        string? value,
        LabelFormat defaultValue = LabelFormat.Normal)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        var normalized = value
            .Trim()
            .ToUpperInvariant()
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "")
            .Replace("Á", "A")
            .Replace("É", "E")
            .Replace("Í", "I")
            .Replace("Ó", "O")
            .Replace("Ú", "U");

        return normalized switch
        {
            "NORMAL" => LabelFormat.Normal,
            "REDUCIDO" => LabelFormat.Reducido,
            "SUPERREDUCIDO" => LabelFormat.SuperReducido,
            "MASREDUCIDO" => LabelFormat.SuperReducido,
            _ => defaultValue
        };
    }
}

/// <summary>
/// Petición para marcar etiquetas como impresas.
/// </summary>
public sealed class MarkLabelsAsPrintedRequest
{
    public List<long> RowIds { get; set; } = [];

    public string? User { get; set; }
}

/// <summary>
/// Resultado de marcado de etiquetas.
/// </summary>
public sealed class MarkLabelsAsPrintedResult
{
    public bool Success { get; set; }

    public int RequestedCount { get; set; }

    public int UpdatedCount { get; set; }

    public string? Message { get; set; }
}