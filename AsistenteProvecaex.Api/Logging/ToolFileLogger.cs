using System.Text;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Servicio encargado de escribir logs diarios
/// de ejecución del motor de herramientas.
///
/// Utiliza el mismo directorio configurado para ApiLogging.
/// Genera ficheros:
/// tools-YYYY-MM-DD.log
/// </summary>
public sealed class ToolFileLogger
{
    private readonly ApiLoggingOptions _options;

    private readonly SemaphoreSlim _lock = new(1, 1);

    public ToolFileLogger(IConfiguration configuration)
    {
        _options = configuration
            .GetSection("ApiLogging")
            .Get<ApiLoggingOptions>() ?? new ApiLoggingOptions();
    }

    /// <summary>
    /// Escribe una entrada de log de herramienta.
    /// </summary>
    public async Task LogAsync(ToolExecutionLogEntry entry)
    {
        if (!_options.Enabled)
            return;

        Directory.CreateDirectory(_options.Directory);

        var fileName = $"tools-{DateTime.Now:yyyy-MM-dd}.log";
        var filePath = Path.Combine(_options.Directory, fileName);

        var line =
            $"{entry.Date:yyyy-MM-dd HH:mm:ss.fff} | " +
            $"Owner={Compact(entry.OwnerKey, 120)} | " +
            $"Machine={Compact(entry.MachineName, 80)} | " +
            $"Tool={Compact(entry.ToolName, 120)} | " +
            $"Executed={entry.Executed} | " +
            $"Success={entry.Success} | " +
            $"Elapsed={entry.ElapsedMs}ms | " +
            $"Args={Compact(entry.ArgumentsJson, 500)} | " +
            $"Result={Compact(entry.Result, 500)}";

        await _lock.WaitAsync();

        try
        {
            await File.AppendAllTextAsync(
                filePath,
                line + Environment.NewLine,
                Encoding.UTF8
            );
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Convierte textos largos o multilínea
    /// en una versión compacta adecuada para un log de una sola línea.
    /// </summary>
    private static string Compact(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "-";

        var compact = value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("  ", " ")
            .Trim();

        if (compact.Length <= maxLength)
            return compact;

        return compact[..maxLength] + "...";
    }
}