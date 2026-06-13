using Asistente.Desktop.Configuration;
using Asistente.Desktop.Models;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Asistente.Desktop.Services;

/// <summary>
/// Cliente Desktop para generar carteles PDF desde la API.
///
/// Primera versión:
/// - Usa el endpoint temporal /api/posters/generate-test/{code}.
/// - Guarda el PDF devuelto en una carpeta temporal local.
/// </summary>
public sealed class PosterGeneratorService
{
    private readonly HttpClient _httpClient;
    private readonly PosterGeneratorConfig _config;

    public PosterGeneratorService(
        HttpClient httpClient,
        PosterGeneratorConfig config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    /// <summary>
    /// Solicita a la API la generación de un cartel PDF
    /// para el código indicado.
    /// </summary>
    public async Task<PosterGenerationResult> GeneratePosterAsync(
        string productCode,
        string posterSize,
        string priceType,
        CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            return new PosterGenerationResult
            {
                Success = false,
                Message = "El generador de carteles está deshabilitado."
            };
        }

        if (string.IsNullOrWhiteSpace(productCode))
        {
            return new PosterGenerationResult
            {
                Success = false,
                Message = "Introduce un código de producto o EAN."
            };
        }

        var safeCode =
            Uri.EscapeDataString(productCode.Trim());

        var safeSize =
            Uri.EscapeDataString(
                string.IsNullOrWhiteSpace(posterSize)
                    ? "A4"
                    : posterSize.Trim()
            );

        var safePriceType =
            Uri.EscapeDataString(
                string.IsNullOrWhiteSpace(priceType)
                    ? "Normal"
                    : priceType.Trim()
            );

        var requestUrl =
            $"{_config.ApiGeneratePosterUrl.TrimEnd('/')}/{safeCode}?size={safeSize}&priceType={safePriceType}";

        try
        {
            using var response =
                await _httpClient.GetAsync(
                    requestUrl,
                    cancellationToken
                );

            if (!response.IsSuccessStatusCode)
            {
                var errorText =
                    await response.Content.ReadAsStringAsync(
                        cancellationToken
                    );

                return new PosterGenerationResult
                {
                    Success = false,
                    Message =
                        $"No se ha podido generar el cartel. HTTP {(int)response.StatusCode}. {errorText}"
                };
            }

            var pdfBytes =
                await response.Content.ReadAsByteArrayAsync(
                    cancellationToken
                );

            if (pdfBytes.Length == 0)
            {
                return new PosterGenerationResult
                {
                    Success = false,
                    Message = "La API ha devuelto un PDF vacío."
                };
            }

            var tempDirectory = Path.Combine(
                Path.GetTempPath(),
                _config.TempFolderName
            );

            Directory.CreateDirectory(tempDirectory);

            var fileName =
                $"cartel-{productCode.Trim()}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf";

            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidChar, '_');
            }

            var fullPath =
                Path.Combine(tempDirectory, fileName);

            await File.WriteAllBytesAsync(
                fullPath,
                pdfBytes,
                cancellationToken
            );

            return new PosterGenerationResult
            {
                Success = true,
                Message = $"Cartel generado correctamente: {fileName}",
                GeneratedPdf = new QuickPrintSelectedFile
                {
                    FullPath = fullPath,
                    FileName = fileName
                }
            };
        }
        catch (Exception ex)
        {
            return new PosterGenerationResult
            {
                Success = false,
                Message =
                    $"Error generando el cartel: {ex.Message}"
            };
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Busca productos usando la API del generador de carteles.
    ///
    /// Tipos válidos:
    /// - Ean
    /// - ArticleCode
    /// - Name
    /// </summary>
    public async Task<List<PosterProductItem>> SearchProductsAsync(
        string searchType,
        string query,
        int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
            return [];

        if (string.IsNullOrWhiteSpace(query))
            return [];

        var safeType =
            Uri.EscapeDataString(searchType.Trim());

        var safeQuery =
            Uri.EscapeDataString(query.Trim());

        var requestUrl =
            $"{_config.ApiSearchProductsUrl.TrimEnd('/')}?type={safeType}&query={safeQuery}&maxResults={maxResults}";

        using var response =
            await _httpClient.GetAsync(
                requestUrl,
                cancellationToken
            );

        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var json =
            await response.Content.ReadAsStringAsync(
                cancellationToken
            );

        var result =
            JsonSerializer.Deserialize<PosterProductSearchResponse>(
                json,
                JsonOptions
            );

        return result?.Products ?? [];
    }

    /// <summary>
    /// Solicita a la API la generación de un PDF de carteles
    /// con artículos que pueden tener distinto tamaño y oferta.
    ///
    /// Cada entrada del lote incluye:
    /// - Código de producto.
    /// - Tamaño.
    /// - Tipo de oferta.
    /// </summary>
    public async Task<PosterGenerationResult> GeneratePosterBatchAsync(
        IReadOnlyList<PosterSelectedProductItem> selectedItems,
        CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            return new PosterGenerationResult
            {
                Success = false,
                Message = "El generador de carteles está deshabilitado."
            };
        }

        var items = selectedItems
            .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
            .Select(item => new PosterBatchGenerationItemRequest
            {
                ProductCode = item.ProductCode.Trim(),
                Size = string.IsNullOrWhiteSpace(item.Size)
                    ? "A4"
                    : item.Size.Trim(),
                PriceType = string.IsNullOrWhiteSpace(item.PriceType)
                    ? "Normal"
                    : item.PriceType.Trim()
            })
            .ToList();

        if (items.Count == 0)
        {
            return new PosterGenerationResult
            {
                Success = false,
                Message = "No hay productos válidos para generar carteles."
            };
        }

        var request = new PosterBatchGenerationRequest
        {
            Items = items
        };

        try
        {
            var json =
                JsonSerializer.Serialize(request);

            using var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

            using var response =
                await _httpClient.PostAsync(
                    _config.ApiGeneratePosterBatchUrl,
                    content,
                    cancellationToken
                );

            if (!response.IsSuccessStatusCode)
            {
                var errorText =
                    await response.Content.ReadAsStringAsync(
                        cancellationToken
                    );

                return new PosterGenerationResult
                {
                    Success = false,
                    Message =
                        $"No se ha podido generar el lote de carteles. HTTP {(int)response.StatusCode}. {errorText}"
                };
            }

            var pdfBytes =
                await response.Content.ReadAsByteArrayAsync(
                    cancellationToken
                );

            if (pdfBytes.Length == 0)
            {
                return new PosterGenerationResult
                {
                    Success = false,
                    Message = "La API ha devuelto un PDF vacío."
                };
            }

            var tempDirectory = Path.Combine(
                Path.GetTempPath(),
                _config.TempFolderName
            );

            Directory.CreateDirectory(tempDirectory);

            var fileName =
                $"carteles-mixto-{DateTime.Now:yyyyMMdd-HHmmss}.pdf";

            var fullPath =
                Path.Combine(
                    tempDirectory,
                    fileName
                );

            await File.WriteAllBytesAsync(
                fullPath,
                pdfBytes,
                cancellationToken
            );

            return new PosterGenerationResult
            {
                Success = true,
                Message =
                    $"PDF mixto generado correctamente. Artículos: {items.Count}.",
                GeneratedPdf = new QuickPrintSelectedFile
                {
                    FullPath = fullPath,
                    FileName = fileName
                }
            };
        }
        catch (Exception ex)
        {
            return new PosterGenerationResult
            {
                Success = false,
                Message =
                    $"Error generando el lote mixto de carteles: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Obtiene desde la API las etiquetas pendientes de impresión
    /// del año actual.
    ///
    /// De momento solo se trabaja con el almacén 01.
    /// </summary>
    public async Task<List<PendingLabelItem>> GetPendingLabelsAsync(
        string warehouse = "01",
        int maxResults = 1000,
        CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
            return [];

        var safeWarehouse =
            Uri.EscapeDataString(
                string.IsNullOrWhiteSpace(warehouse)
                    ? "01"
                    : warehouse.Trim()
            );

        var requestUrl =
            $"{_config.ApiPendingLabelsUrl.TrimEnd('/')}?warehouse={safeWarehouse}&maxResults={maxResults}";

        using var response =
            await _httpClient.GetAsync(
                requestUrl,
                cancellationToken
            );

        if (!response.IsSuccessStatusCode)
            return [];

        var json =
            await response.Content.ReadAsStringAsync(
                cancellationToken
            );

        var result =
            JsonSerializer.Deserialize<PendingLabelsResponse>(
                json,
                JsonOptions
            );

        return result?.Labels ?? [];
    }

    /// <summary>
    /// Solicita a la API la generación de un PDF con las etiquetas
    /// seleccionadas por el usuario.
    ///
    /// De momento NO marca etiquetas como impresas.
    /// Solo genera el PDF para previsualizar o imprimir.
    /// </summary>
    public async Task<PosterGenerationResult> GenerateLabelsPdfAsync(
        IReadOnlyList<PendingLabelItem> selectedLabels,
        string labelFormat,
        CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            return new PosterGenerationResult
            {
                Success = false,
                Message = "El generador de etiquetas está deshabilitado."
            };
        }

        var rowIds = selectedLabels
            .Where(label => label.ReturnRowId > 0)
            .Select(label => label.ReturnRowId)
            .Distinct()
            .ToList();

        if (rowIds.Count == 0)
        {
            return new PosterGenerationResult
            {
                Success = false,
                Message = "No hay etiquetas válidas para generar el PDF."
            };
        }

        var request = new LabelBatchGenerationRequest
        {
            RowIds = rowIds,
            Format = string.IsNullOrWhiteSpace(labelFormat)
                ? "Normal"
                : labelFormat.Trim()
        };

        try
        {
            var json =
                JsonSerializer.Serialize(request);

            using var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

            using var response =
                await _httpClient.PostAsync(
                    _config.ApiGenerateLabelsUrl,
                    content,
                    cancellationToken
                );

            if (!response.IsSuccessStatusCode)
            {
                var errorText =
                    await response.Content.ReadAsStringAsync(
                        cancellationToken
                    );

                return new PosterGenerationResult
                {
                    Success = false,
                    Message =
                        $"No se ha podido generar el PDF de etiquetas. HTTP {(int)response.StatusCode}. {errorText}"
                };
            }

            var pdfBytes =
                await response.Content.ReadAsByteArrayAsync(
                    cancellationToken
                );

            if (pdfBytes.Length == 0)
            {
                return new PosterGenerationResult
                {
                    Success = false,
                    Message = "La API ha devuelto un PDF de etiquetas vacío."
                };
            }

            var tempDirectory = Path.Combine(
                Path.GetTempPath(),
                _config.TempFolderName
            );

            Directory.CreateDirectory(tempDirectory);

            var fileName =
               $"etiquetas-{request.Format}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf";

            var fullPath =
                Path.Combine(
                    tempDirectory,
                    fileName
                );

            await File.WriteAllBytesAsync(
                fullPath,
                pdfBytes,
                cancellationToken
            );

            return new PosterGenerationResult
            {
                Success = true,
                Message =
                    $"PDF de etiquetas generado correctamente. Etiquetas: {rowIds.Count}.",
                GeneratedPdf = new QuickPrintSelectedFile
                {
                    FullPath = fullPath,
                    FileName = fileName
                }
            };
        }
        catch (Exception ex)
        {
            return new PosterGenerationResult
            {
                Success = false,
                Message =
                    $"Error generando el PDF de etiquetas: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Busca etiquetas de reposición por EAN, código artículo o nombre.
    /// </summary>
    public async Task<List<PendingLabelItem>> SearchLabelsAsync(
        string searchType,
        string query,
        string warehouse = "01",
        int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
            return [];

        if (string.IsNullOrWhiteSpace(query))
            return [];

        var safeType =
            Uri.EscapeDataString(searchType.Trim());

        var safeQuery =
            Uri.EscapeDataString(query.Trim());

        var safeWarehouse =
            Uri.EscapeDataString(
                string.IsNullOrWhiteSpace(warehouse)
                    ? "01"
                    : warehouse.Trim()
            );

        var requestUrl =
            $"{_config.ApiSearchLabelsUrl.TrimEnd('/')}?type={safeType}&query={safeQuery}&warehouse={safeWarehouse}&maxResults={maxResults}";

        using var response =
            await _httpClient.GetAsync(
                requestUrl,
                cancellationToken
            );

        if (!response.IsSuccessStatusCode)
            return [];

        var json =
            await response.Content.ReadAsStringAsync(
                cancellationToken
            );

        var result =
            JsonSerializer.Deserialize<PendingLabelsResponse>(
                json,
                JsonOptions
            );

        return result?.Labels ?? [];
    }

    /// <summary>
    /// Marca como impresas las etiquetas seleccionadas.
    /// </summary>
    public async Task<MarkLabelsAsPrintedResult> MarkLabelsAsPrintedAsync(
        IReadOnlyList<PendingLabelItem> labels,
        CancellationToken cancellationToken = default)
    {
        var rowIds = labels
            .Where(label => label.ReturnRowId > 0)
            .Select(label => label.ReturnRowId)
            .Distinct()
            .ToList();

        if (rowIds.Count == 0)
        {
            return new MarkLabelsAsPrintedResult
            {
                Success = false,
                RequestedCount = 0,
                UpdatedCount = 0,
                Message = "No hay etiquetas válidas para marcar como impresas."
            };
        }

        var request = new MarkLabelsAsPrintedRequest
        {
            RowIds = rowIds,
            User = Environment.UserName
        };

        var json =
            JsonSerializer.Serialize(request);

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"
        );

        using var response =
            await _httpClient.PostAsync(
                _config.ApiMarkLabelsAsPrintedUrl,
                content,
                cancellationToken
            );

        var responseText =
            await response.Content.ReadAsStringAsync(
                cancellationToken
            );

        if (!response.IsSuccessStatusCode)
        {
            return new MarkLabelsAsPrintedResult
            {
                Success = false,
                RequestedCount = rowIds.Count,
                UpdatedCount = 0,
                Message = $"Error marcando etiquetas como impresas. HTTP {(int)response.StatusCode}. {responseText}"
            };
        }

        var result =
            JsonSerializer.Deserialize<MarkLabelsAsPrintedResult>(
                responseText,
                JsonOptions
            );

        return result ?? new MarkLabelsAsPrintedResult
        {
            Success = false,
            RequestedCount = rowIds.Count,
            UpdatedCount = 0,
            Message = "La API no devolvió una respuesta válida."
        };
    }
}