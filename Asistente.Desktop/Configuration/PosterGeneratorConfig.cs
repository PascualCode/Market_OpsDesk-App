namespace Asistente.Desktop.Configuration;

/// <summary>
/// Configuración del generador de carteles y etiquetas.
/// </summary>
public sealed class PosterGeneratorConfig
{
    /// <summary>
    /// Activa o desactiva el módulo de carteles.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Endpoint temporal que genera un cartel PDF a partir de un código.
    /// En fases posteriores se sustituirá por el endpoint definitivo.
    /// </summary>
    public string ApiGeneratePosterUrl { get; set; } =
        "http://10.0.0.210:5055/api/posters/generate-test";

    /// <summary>
    /// Carpeta temporal local donde el Desktop guardará
    /// los PDFs generados antes de imprimirlos.
    /// </summary>
    public string TempFolderName { get; set; } =
        "Asistente\\Carteles";

    /// <summary>
    /// Endpoint de búsqueda de productos para el generador de carteles.
    /// </summary>
    public string ApiSearchProductsUrl { get; set; } =
        "http://10.0.0.210:5055/api/posters/products/search";

    /// <summary>
    /// Endpoint multiartículo para generar carteles en lote.
    /// </summary>
    public string ApiGeneratePosterBatchUrl { get; set; } =
        "http://10.0.0.210:5055/api/posters/generate-batch";

    /// <summary>
    /// Endpoint para obtener etiquetas pendientes de impresión.
    /// </summary>
    public string ApiPendingLabelsUrl { get; set; } =
        "http://10.0.0.210:5055/api/labels/pending";

    /// <summary>
    /// Endpoint para generar PDF de etiquetas seleccionadas.
    /// </summary>
    public string ApiGenerateLabelsUrl { get; set; } =
        "http://10.0.0.210:5055/api/labels/generate";

    /// <summary>
    /// Endpoint para buscar etiquetas de reposición.
    /// </summary>
    public string ApiSearchLabelsUrl { get; set; } =
        "http://10.0.0.210:5055/api/labels/search";

    /// <summary>
    /// Endpoint para marcar etiquetas como impresas.
    /// </summary>
    public string ApiMarkLabelsAsPrintedUrl { get; set; } =
        "http://10.0.0.210:5055/api/labels/mark-printed";
}