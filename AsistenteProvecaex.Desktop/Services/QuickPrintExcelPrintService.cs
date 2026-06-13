using Asistente.Desktop.Models;
using System.IO;
using System.Runtime.InteropServices;

namespace Asistente.Desktop.Services;

/// <summary>
/// Servicio de impresión rápida para documentos Excel.
///
/// Estrategia:
/// - Abre Excel en segundo plano.
/// - Exporta el libro a un PDF temporal.
/// - Cierra Excel.
/// - Imprime el PDF usando el mismo servicio que ya funciona con Adobe Reader.
/// </summary>
public sealed class QuickPrintExcelPrintService
{
    private readonly QuickPrintPdfPrintService _pdfPrintService;

    public QuickPrintExcelPrintService(
        QuickPrintPdfPrintService pdfPrintService)
    {
        _pdfPrintService = pdfPrintService;
    }

    public QuickPrintExecutionResult PrintExcelDocument(
        QuickPrintSelectedFile selectedFile,
        QuickPrintPrinterItem printer)
    {
        if (selectedFile is null ||
            string.IsNullOrWhiteSpace(selectedFile.FullPath))
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message = "No hay ningún documento Excel válido preparado para imprimir."
            };
        }

        if (!File.Exists(selectedFile.FullPath))
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message = "El documento Excel seleccionado ya no existe o no es accesible."
            };
        }

        var extension = Path.GetExtension(
            selectedFile.FullPath
        ).ToLowerInvariant();

        if (extension != ".xlsx" &&
            extension != ".xls")
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message = $"La extensión '{extension}' no es un documento Excel compatible."
            };
        }

        Type? excelType =
            Type.GetTypeFromProgID("Excel.Application");

        if (excelType is null)
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message = "No se ha encontrado Microsoft Excel instalado en este equipo."
            };
        }

        var tempPdfPath = Path.Combine(
            Path.GetTempPath(),
            $"quickprint-excel-{Guid.NewGuid():N}.pdf"
        );

        dynamic? excelApp = null;
        dynamic? workbook = null;

        try
        {
            excelApp = Activator.CreateInstance(excelType);

            if (excelApp is null)
            {
                return new QuickPrintExecutionResult
                {
                    Success = false,
                    Message = "No se ha podido iniciar Microsoft Excel."
                };
            }

            excelApp.Visible = false;
            excelApp.DisplayAlerts = false;

            workbook = excelApp.Workbooks.Open(
                selectedFile.FullPath,
                ReadOnly: true
            );

            // 0 = xlTypePDF
            workbook.ExportAsFixedFormat(
                0,
                tempPdfPath
            );

            workbook.Close(
                SaveChanges: false
            );

            workbook = null;

            excelApp.Quit();
            excelApp = null;

            if (!File.Exists(tempPdfPath))
            {
                return new QuickPrintExecutionResult
                {
                    Success = false,
                    Message = "Excel no ha generado el PDF temporal para imprimir."
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
                        $"El documento Excel se convirtió a PDF, pero no se pudo imprimir: {printResult.Message}"
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
                    $"No se ha podido convertir el documento Excel a PDF para imprimir: {ex.Message}"
            };
        }
        finally
        {
            try
            {
                if (workbook is not null)
                {
                    workbook.Close(
                        SaveChanges: false
                    );
                }
            }
            catch
            {
            }

            try
            {
                if (excelApp is not null)
                {
                    excelApp.Quit();
                }
            }
            catch
            {
            }

            try
            {
                if (workbook is not null)
                {
                    Marshal.FinalReleaseComObject(workbook);
                }
            }
            catch
            {
            }

            try
            {
                if (excelApp is not null)
                {
                    Marshal.FinalReleaseComObject(excelApp);
                }
            }
            catch
            {
            }

            ScheduleTemporaryPdfDeletion(
                tempPdfPath,
                TimeSpan.FromMinutes(5)
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
