using System.Text;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Servicio encargado de escribir logs básicos de actividad de la API
/// en ficheros diarios.
///
/// Cada día se genera un archivo como:
/// api-2026-05-14.log
/// </summary>
public sealed class ApiFileLogger
{
    private readonly ApiLoggingOptions _options;

    /// <summary>
    /// Semáforo para evitar escrituras simultáneas
    /// sobre el mismo fichero de log.
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ApiFileLogger(IConfiguration configuration)
    {
        _options = configuration
            .GetSection("ApiLogging")
            .Get<ApiLoggingOptions>() ?? new ApiLoggingOptions();
    }

    /// <summary>
    /// Escribe una entrada de log en el fichero correspondiente al día actual.
    /// </summary>
    public async Task LogAsync(ApiLogEntry entry)
    {
        if (!_options.Enabled)
            return;

        Directory.CreateDirectory(_options.Directory);

        var fileName = $"api-{DateTime.Now:yyyy-MM-dd}.log";
        var filePath = Path.Combine(_options.Directory, fileName);

        var line =
            $"{entry.Date:yyyy-MM-dd HH:mm:ss.fff} | " +
            $"IP={entry.ClientIp} | " +
            $"{entry.Method} {entry.Path} | " +
            $"Status={entry.StatusCode} | " +
            $"Model={entry.Model ?? "-"} | " +
            $"QuestionLength={entry.QuestionLength?.ToString() ?? "-"} | " +
            $"Elapsed={entry.ElapsedMs}ms | " +
            $"Error={entry.Error ?? "-"}";

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
}
