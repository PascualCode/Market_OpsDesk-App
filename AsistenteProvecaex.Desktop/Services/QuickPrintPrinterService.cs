using Asistente.Desktop.Models;
using System.Drawing.Printing;
using System.Management;

namespace Asistente.Desktop.Services;

/// <summary>
/// Obtiene las impresoras instaladas en el equipo
/// para mostrarlas en la Drop Zone de impresión rápida.
///
/// Los nombres visibles se obtienen de Windows.
/// Además, se completan driver y puerto mediante WMI,
/// porque Adobe Reader los necesita para la impresión /t.
/// </summary>
public sealed class QuickPrintPrinterService
{
    /// <summary>
    /// Devuelve la lista de impresoras instaladas en Windows,
    /// enriquecidas con DriverName y PortName cuando estén disponibles.
    /// </summary>
    public List<QuickPrintPrinterItem> GetInstalledPrinters()
    {

        var defaultPrinterName = "";

        try
        {
            var printerSettings = new PrinterSettings();
            defaultPrinterName = printerSettings.PrinterName;
        }
        catch
        {
            defaultPrinterName = "";
        }

        var printerDetails = LoadPrinterDetailsFromWmi();

        var printers = new List<QuickPrintPrinterItem>();

        foreach (string printerName in PrinterSettings.InstalledPrinters)
        {
            if (string.IsNullOrWhiteSpace(printerName))
                continue;

            printerDetails.TryGetValue(
                printerName,
                out var details
            );

            printers.Add(new QuickPrintPrinterItem
            {
                DisplayName = printerName,
                PrinterName = printerName,
                DriverName = details?.DriverName ?? "",
                PortName = details?.PortName ?? "",
                IsDefault = string.Equals(
                    printerName,
                    defaultPrinterName,
                    StringComparison.OrdinalIgnoreCase
                )
            });
        }

        return printers
            .OrderBy(printer => printer.DisplayName)
            .ToList();
    }

    /// <summary>
    /// Lee de WMI la información adicional de impresoras:
    /// - Nombre
    /// - Driver
    /// - Puerto
    /// </summary>
    private static Dictionary<string, PrinterWmiDetails> LoadPrinterDetailsFromWmi()
    {
        var result = new Dictionary<string, PrinterWmiDetails>(
            StringComparer.OrdinalIgnoreCase
        );

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DriverName, PortName FROM Win32_Printer"
            );

            using var collection = searcher.Get();

            foreach (ManagementObject printer in collection)
            {
                var name =
                    printer["Name"]?.ToString() ?? "";

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                result[name] = new PrinterWmiDetails
                {
                    DriverName =
                        printer["DriverName"]?.ToString() ?? "",

                    PortName =
                        printer["PortName"]?.ToString() ?? ""
                };
            }
        }
        catch
        {
            // Si WMI falla, devolvemos diccionario vacío.
            // Las impresoras seguirán apareciendo en el combo,
            // pero la impresión mostrará un error claro
            // si faltan driver o puerto.
        }

        return result;
    }

    /// <summary>
    /// Datos técnicos internos recuperados desde WMI.
    /// </summary>
    private sealed class PrinterWmiDetails
    {
        public string DriverName { get; set; } = "";

        public string PortName { get; set; } = "";
    }
}