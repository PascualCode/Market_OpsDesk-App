using Asistente.Desktop.Models;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;

namespace Asistente.Desktop.Services;

/// <summary>
/// Servicio de impresión rápida para imágenes:
/// - PNG
/// - JPG
/// - JPEG
///
/// Utiliza impresión nativa .NET mediante PrintDocument.
/// La imagen se ajusta a la página conservando proporción.
/// </summary>
public sealed class QuickPrintImagePrintService
{
    /// <summary>
    /// Envía una imagen a la impresora seleccionada.
    /// </summary>
    public QuickPrintExecutionResult PrintImage(
        QuickPrintSelectedFile selectedFile,
        QuickPrintPrinterItem printer)
    {
        if (selectedFile is null ||
            string.IsNullOrWhiteSpace(selectedFile.FullPath))
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message = "No hay ninguna imagen válida preparada para imprimir."
            };
        }

        if (!File.Exists(selectedFile.FullPath))
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message = "La imagen seleccionada ya no existe o no es accesible."
            };
        }

        if (printer is null ||
            string.IsNullOrWhiteSpace(printer.PrinterName))
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message = "No se ha seleccionado una impresora válida."
            };
        }

        try
        {
            using var image =
                Image.FromFile(selectedFile.FullPath);

            using var printDocument =
                new PrintDocument();

            printDocument.DocumentName =
                selectedFile.FileName;

            printDocument.PrinterSettings.PrinterName =
                printer.PrinterName;

            if (!printDocument.PrinterSettings.IsValid)
            {
                return new QuickPrintExecutionResult
                {
                    Success = false,
                    Message =
                        $"La impresora '{printer.DisplayName}' no es válida o no está disponible."
                };
            }

            printDocument.PrintPage += (_, e) =>
            {
                var bounds = e.MarginBounds;

                var imageRatio =
                    (double)image.Width / image.Height;

                var pageRatio =
                    (double)bounds.Width / bounds.Height;

                int drawWidth;
                int drawHeight;

                if (imageRatio > pageRatio)
                {
                    drawWidth = bounds.Width;
                    drawHeight = (int)(bounds.Width / imageRatio);
                }
                else
                {
                    drawHeight = bounds.Height;
                    drawWidth = (int)(bounds.Height * imageRatio);
                }

                var drawX =
                    bounds.Left + ((bounds.Width - drawWidth) / 2);

                var drawY =
                    bounds.Top + ((bounds.Height - drawHeight) / 2);

                e.Graphics.DrawImage(
                    image,
                    drawX,
                    drawY,
                    drawWidth,
                    drawHeight
                );

                e.HasMorePages = false;
            };

            printDocument.Print();

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
                    $"No se ha podido imprimir la imagen: {ex.Message}"
            };
        }
    }
}