using Asistente.Desktop.Configuration;
using Asistente.Desktop.Models;
using System.IO;

namespace Asistente.Desktop.Services;

/// <summary>
/// Valida y transforma archivos arrastrados a la Drop Zone
/// de impresión rápida.
///
/// Primera versión:
/// - Solo un archivo cada vez.
/// - Solo extensiones permitidas en config.json.
/// - Comprueba que el archivo exista físicamente.
/// </summary>
public sealed class QuickPrintFileSelectionService
{
    private readonly QuickPrintConfig _config;

    public QuickPrintFileSelectionService(
        QuickPrintConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Intenta convertir los archivos arrastrados en un PDF
    /// válido para el módulo de impresión rápida.
    /// </summary>
    public bool TrySelectFile(
        string[]? filePaths,
        out QuickPrintSelectedFile? selectedFile,
        out string message)
    {
        selectedFile = null;

        if (!_config.Enabled)
        {
            message = "La impresión rápida está deshabilitada.";
            return false;
        }

        if (filePaths is null || filePaths.Length == 0)
        {
            message = "No se ha recibido ningún archivo.";
            return false;
        }

        if (filePaths.Length > 1)
        {
            message = "Arrastra solo un archivo PDF cada vez.";
            return false;
        }

        var fullPath = filePaths[0];

        if (string.IsNullOrWhiteSpace(fullPath))
        {
            message = "La ruta del archivo no es válida.";
            return false;
        }

        if (!File.Exists(fullPath))
        {
            message = "El archivo seleccionado no existe o no es accesible.";
            return false;
        }

        var extension = Path.GetExtension(fullPath);

        var allowedExtensions = _config.AllowedExtensions
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .ToHashSet();

        if (!allowedExtensions.Contains(extension.ToLowerInvariant()))
        {
            var allowedText = string.Join(
                ", ",
                allowedExtensions
            );

            message =
                $"Extensión no permitida. Archivos aceptados: {allowedText}.";
            return false;
        }

        selectedFile = new QuickPrintSelectedFile
        {
            FullPath = fullPath,
            FileName = Path.GetFileName(fullPath)
        };

        message =
            $"Archivo preparado: {selectedFile.FileName}";

        return true;
    }

    /// <summary>
    /// Intenta convertir varios archivos arrastrados en una lista válida
    /// para impresión rápida.
    ///
    /// Todos los archivos deben existir y tener una extensión permitida.
    /// </summary>
    public bool TrySelectFiles(
        string[]? filePaths,
        out List<QuickPrintSelectedFile> selectedFiles,
        out string message)
    {
        selectedFiles = [];

        if (!_config.Enabled)
        {
            message = "La impresión rápida está deshabilitada.";
            return false;
        }

        if (filePaths is null || filePaths.Length == 0)
        {
            message = "No se ha recibido ningún archivo.";
            return false;
        }

        var maxBatchFiles = Math.Max(
            _config.MaxBatchFiles,
            1
        );

        if (filePaths.Length > maxBatchFiles)
        {
            message =
                $"Demasiados archivos. Máximo permitido: {maxBatchFiles}.";
            return false;
        }

        var allowedExtensions = _config.AllowedExtensions
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .ToHashSet();

        foreach (var fullPath in filePaths)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                message = "Una de las rutas recibidas no es válida.";
                selectedFiles.Clear();
                return false;
            }

            if (!File.Exists(fullPath))
            {
                message =
                    $"El archivo no existe o no es accesible: {Path.GetFileName(fullPath)}";
                selectedFiles.Clear();
                return false;
            }

            var extension = Path.GetExtension(fullPath)
                .ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                var allowedText = string.Join(
                    ", ",
                    allowedExtensions
                );

                message =
                    $"Extensión no permitida en '{Path.GetFileName(fullPath)}'. Archivos aceptados: {allowedText}.";
                selectedFiles.Clear();
                return false;
            }

            selectedFiles.Add(new QuickPrintSelectedFile
            {
                FullPath = fullPath,
                FileName = Path.GetFileName(fullPath)
            });
        }

        if (selectedFiles.Count == 1)
        {
            message =
                $"Archivo preparado: {selectedFiles[0].FileName}";
        }
        else
        {
            message =
                $"Archivos preparados: {selectedFiles.Count}.";
        }

        return true;
    }
}
