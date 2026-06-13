using Asistente.Desktop.Models;
using System.IO;
using System.Runtime.InteropServices;

namespace Asistente.Desktop.Services;

/// <summary>
/// Servicio de impresión rápida para documentos Word.
///
/// Estrategia:
/// - Abre Word en segundo plano.
/// - Exporta el documento a un PDF temporal.
/// - Cierra Word.
/// - Imprime el PDF usando el mismo servicio que ya funciona con Adobe Reader.
/// </summary>
public sealed class QuickPrintWordPrintService
{
    private readonly QuickPrintPdfPrintService _pdfPrintService;

    public QuickPrintWordPrintService(
        QuickPrintPdfPrintService pdfPrintService)
    {
        _pdfPrintService = pdfPrintService;
    }

    public QuickPrintExecutionResult PrintWordDocument(
        QuickPrintSelectedFile selectedFile,
        QuickPrintPrinterItem printer)
    {
        if (selectedFile is null ||
            string.IsNullOrWhiteSpace(selectedFile.FullPath))
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message = "No hay ningún documento Word válido preparado para imprimir."
            };
        }

        if (!File.Exists(selectedFile.FullPath))
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message = "El documento Word seleccionado ya no existe o no es accesible."
            };
        }

        var extension = Path.GetExtension(
            selectedFile.FullPath
        ).ToLowerInvariant();

        if (extension != ".docx" && extension != ".doc")
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message = $"La extensión '{extension}' no es un documento Word compatible."
            };
        }

        Type? wordType = Type.GetTypeFromProgID("Word.Application");

        if (wordType is null)
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message = "No se ha encontrado Microsoft Word instalado en este equipo."
            };
        }

        var tempPdfPath = Path.Combine(
            Path.GetTempPath(),
            $"quickprint-word-{Guid.NewGuid():N}.pdf"
        );

        dynamic? wordApp = null;
        dynamic? document = null;

        try
        {
            wordApp = Activator.CreateInstance(wordType);

            if (wordApp is null)
            {
                return new QuickPrintExecutionResult
                {
                    Success = false,
                    Message = "No se ha podido iniciar Microsoft Word."
                };
            }

            wordApp.Visible = false;
            wordApp.DisplayAlerts = 0;

            document = wordApp.Documents.Open(
                FileName: selectedFile.FullPath,
                ConfirmConversions: false,
                ReadOnly: true,
                AddToRecentFiles: false,
                Visible: false
            );

            // 17 = wdExportFormatPDF
            document.ExportAsFixedFormat(
                OutputFileName: tempPdfPath,
                ExportFormat: 17,
                OpenAfterExport: false
            );

            document.Close(
                SaveChanges: false
            );

            document = null;

            wordApp.Quit(
                SaveChanges: false
            );

            wordApp = null;

            if (!File.Exists(tempPdfPath))
            {
                return new QuickPrintExecutionResult
                {
                    Success = false,
                    Message = "Word no ha generado el PDF temporal para imprimir."
                };
            }

            var tempPdfFile = new QuickPrintSelectedFile
            {
                FullPath = tempPdfPath,
                FileName = selectedFile.FileName
            };

            var printResult =
                _pdfPrintService.PrintPdf(
                    tempPdfFile,
                    printer
                );

            if (!printResult.Success)
            {
                return new QuickPrintExecutionResult
                {
                    Success = false,
                    Message =
                        $"El documento Word se convirtió a PDF, pero no se pudo imprimir: {printResult.Message}"
                };
            }

            return new QuickPrintExecutionResult
            {
                Success = true,
                Message =
                    $"Orden de impresión enviada: {selectedFile.FileName} → {printer.DisplayName}."
            };
        }
        catch (Exception ex)
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message =
                    $"No se ha podido convertir el documento Word a PDF para imprimir: {ex.Message}"
            };
        }
        finally
        {
            try
            {
                if (document is not null)
                {
                    document.Close(
                        SaveChanges: false
                    );
                }
            }
            catch
            {
            }

            try
            {
                if (wordApp is not null)
                {
                    wordApp.Quit(
                        SaveChanges: false
                    );
                }
            }
            catch
            {
            }

            try
            {
                if (document is not null)
                {
                    Marshal.FinalReleaseComObject(document);
                }
            }
            catch
            {
            }

            try
            {
                if (wordApp is not null)
                {
                    Marshal.FinalReleaseComObject(wordApp);
                }
            }
            catch
            {
            }

            ScheduleTemporaryPdfDeletion(
                tempPdfPath,
                TimeSpan.FromMinutes(1)
            );
        }
    }
    /// <summary>
    /// Programa el borrado diferido del PDF temporal.
    ///
    /// No se borra inmediatamente porque Adobe Reader puede necesitar
    /// unos segundos para abrir y enviar el PDF a la cola de impresión.
    /// </summary>
    private static void ScheduleTemporaryPdfDeletion(
        string tempPdfPath,
        TimeSpan delay)
    {
        if (string.IsNullOrWhiteSpace(tempPdfPath))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay);

                if (File.Exists(tempPdfPath))
                {
                    File.Delete(tempPdfPath);
                }
            }
            catch
            {
                // No bloqueamos la aplicación si el temporal no se puede borrar.
            }
        });
    }
}