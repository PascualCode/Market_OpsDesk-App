namespace Asistente.Desktop.Configuration;

/// <summary>
/// Configuración del módulo de impresión rápida.
///
/// Primera versión:
/// - Activar o desactivar la Drop Zone.
/// - Definir extensiones permitidas.
/// - Permitir configurar manualmente la ruta de Adobe Reader
///   si la detección automática no fuese suficiente.
/// </summary>
public sealed class QuickPrintConfig
{
    /// <summary>
    /// Activa o desactiva la función de impresión rápida.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Extensiones de archivo admitidas por la Drop Zone.
    /// </summary>
    public List<string> AllowedExtensions { get; set; } =
    [
        ".pdf",
        ".png",
        ".jpg",
        ".jpeg",
        ".docx",
        ".xlsx",
        ".xls"
    ];

    /// <summary>
    /// Ruta opcional al ejecutable de Adobe Acrobat Reader.
    ///
    /// Si está vacía, el Desktop intentará detectarlo automáticamente.
    ///
    /// Ejemplo:
    /// C:\Program Files\Adobe\Acrobat DC\Acrobat\Acrobat.exe
    /// o
    /// C:\Program Files (x86)\Adobe\Acrobat Reader DC\Reader\AcroRd32.exe
    /// </summary>
    public string AdobeReaderExecutablePath { get; set; } = "";

    /// <summary>
    /// Intenta cerrar Adobe Reader automáticamente después
    /// de enviar la impresión.
    /// </summary>
    public bool CloseAdobeAfterPrint { get; set; } = true;

    /// <summary>
    /// Segundos de espera antes de cerrar Adobe Reader.
    /// Se da margen para que Adobe envíe el trabajo a la cola.
    /// </summary>
    public int CloseAdobeAfterPrintSeconds { get; set; } = 20;

    /// <summary>
    /// Número máximo de archivos que se pueden arrastrar
    /// e imprimir en una sola tanda.
    /// </summary>
    public int MaxBatchFiles { get; set; } = 10;
}