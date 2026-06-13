using Asistente.Desktop.Models;
using System.IO;

namespace Asistente.Desktop.Services;

/// <summary>
/// Orquestador de impresión rápida.
///
/// Decide qué servicio concreto debe imprimir el archivo
/// según su extensión.
/// </summary>
public sealed class QuickPrintDocumentPrintService
{
    private readonly QuickPrintPdfPrintService _pdfPrintService;
    private readonly QuickPrintImagePrintService _imagePrintService;
    private readonly QuickPrintWordPrintService _wordPrintService;
    private readonly QuickPrintExcelPrintService _excelPrintService;

    public QuickPrintDocumentPrintService(
        QuickPrintPdfPrintService pdfPrintService,
        QuickPrintImagePrintService imagePrintService,
        QuickPrintWordPrintService wordPrintService,
        QuickPrintExcelPrintService excelPrintService)
    {
        _pdfPrintService = pdfPrintService;
        _imagePrintService = imagePrintService;
        _wordPrintService = wordPrintService;
        _excelPrintService = excelPrintService;
    }

    /// <summary>
    /// Imprime el archivo seleccionado usando
    /// el motor adecuado para su extensión.
    /// </summary>
    public QuickPrintExecutionResult Print(
        QuickPrintSelectedFile selectedFile,
        QuickPrintPrinterItem printer)
    {
        if (selectedFile is null ||
            string.IsNullOrWhiteSpace(selectedFile.FullPath))
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message = "No hay ningún archivo válido preparado para imprimir."
            };
        }

        var extension =
            Path.GetExtension(selectedFile.FullPath)
                .ToLowerInvariant();

        return extension switch
        {
            ".pdf" => _pdfPrintService.PrintPdf(
                selectedFile,
                printer
            ),

            ".png" or ".jpg" or ".jpeg" =>
                _imagePrintService.PrintImage(
                    selectedFile,
                    printer
                ),

            ".docx" or ".doc" => _wordPrintService.PrintWordDocument(
                selectedFile,
                printer
            ),

            ".xlsx" or ".xls" => _excelPrintService.PrintExcelDocument(
                selectedFile,
                printer
            ),

            _ => new QuickPrintExecutionResult
            {
                Success = false,
                Message =
                    $"La extensión '{extension}' no tiene un motor de impresión configurado."
            }
        };
    }
}
