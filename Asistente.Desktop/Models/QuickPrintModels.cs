namespace Asistente.Desktop.Models;

/// <summary>
/// Archivo seleccionado o arrastrado a la zona de impresión rápida.
/// </summary>
public sealed class QuickPrintSelectedFile
{
    /// <summary>
    /// Ruta completa del archivo.
    /// </summary>
    public string FullPath { get; set; } = "";

    /// <summary>
    /// Nombre visible del archivo.
    /// </summary>
    public string FileName { get; set; } = "";
}

/// <summary>
/// Impresora disponible para la Drop Zone de impresión rápida.
/// 
/// Además del nombre visible, se guardan:
/// - DriverName
/// - PortName
///
/// Adobe Reader los necesita para imprimir con el comando /t.
/// </summary>
public sealed class QuickPrintPrinterItem
{
    /// <summary>
    /// Nombre visible de la impresora.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Nombre real de la impresora instalado en Windows.
    /// </summary>
    public string PrinterName { get; set; } = "";

    /// <summary>
    /// Nombre del controlador de la impresora.
    /// </summary>
    public string DriverName { get; set; } = "";

    /// <summary>
    /// Puerto asociado a la impresora.
    /// </summary>
    public string PortName { get; set; } = "";

    /// <summary>
    /// Indica si esta impresora es la predeterminada del equipo.
    /// </summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// Resultado de intentar enviar un PDF a impresión rápida.
/// </summary>
public sealed class QuickPrintExecutionResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";
}