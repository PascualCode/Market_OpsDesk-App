using System.Text;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Logger diario de métricas de rendimiento de Ollama.
///
/// Genera ficheros:
/// ollama-performance-YYYY-MM-DD.log
///
/// Utiliza el mismo directorio de logs configurado
/// en ApiLogging.
/// </summary>
public sealed class OllamaPerformanceFileLogger
{
    private readonly ApiLoggingOptions _options;

    private readonly SemaphoreSlim _lock = new(1, 1);

    public OllamaPerformanceFileLogger(
        IConfiguration configuration)
    {
        _options = configuration
            .GetSection("ApiLogging")
            .Get<ApiLoggingOptions>() ?? new ApiLoggingOptions();
    }

    /// <summary>
    /// Escribe una línea de rendimiento de Ollama.
    /// </summary>
    public async Task LogAsync(
        OllamaPerformanceLogEntry entry)
    {
        if (!_options.Enabled)
            return;

        Directory.CreateDirectory(_options.Directory);

        var fileName =
            $"ollama-performance-{DateTime.Now:yyyy-MM-dd}.log";

        var filePath =
            Path.Combine(_options.Directory, fileName);

        var line =
            $"{entry.Date:yyyy-MM-dd HH:mm:ss.fff} | " +
            $"Stage={Compact(entry.Stage, 80)} | " +
            $"Model={Compact(entry.Model, 120)} | " +
            $"Total={FormatMilliseconds(entry.TotalMs)} | " +
            $"Load={FormatMilliseconds(entry.LoadMs)} | " +
            $"PromptTokens={FormatNullable(entry.PromptTokens)} | " +
            $"PromptEval={FormatMilliseconds(entry.PromptEvalMs)} | " +
            $"OutputTokens={FormatNullable(entry.OutputTokens)} | " +
            $"Eval={FormatMilliseconds(entry.EvalMs)}";

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

    private static string FormatMilliseconds(long? value)
    {
        return value.HasValue
            ? $"{value.Value}ms"
            : "-";
    }

    private static string FormatNullable(int? value)
    {
        return value.HasValue
            ? value.Value.ToString()
            : "-";
    }

    /// <summary>
    /// Compacta textos largos para mantener una línea limpia.
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
            .Trim();

        if (compact.Length <= maxLength)
            return compact;

        return compact[..maxLength] + "...";
    }
}