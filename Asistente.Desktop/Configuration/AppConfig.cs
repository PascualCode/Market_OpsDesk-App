using Asistente.Desktop.DesktopTasks;
using Asistente.Desktop.Infrastructure;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Asistente.Desktop.Configuration;

/// <summary>
/// Configuración de la aplicación de escritorio.
///
/// Se carga desde config.json para facilitar despliegues.
/// Así se puede cambiar la URL de la API, el nombre del asistente,
/// programas permitidos o rutas locales sin recompilar.
/// </summary>
public sealed class AppConfig
{
    /// <summary>
    /// Endpoint streaming de la API intermedia.
    /// </summary>
    public string ApiStreamUrl { get; set; } =
        "http://localhost:5169/api/chat/stream";

    /// <summary>
    /// Endpoint de salud de la API intermedia.
    /// </summary>
    public string ApiHealthUrl { get; set; } =
        "http://localhost:5169/health";

    /// <summary>
    /// Nombre visible del asistente en la UI y en la bandeja.
    /// </summary>
    public string AssistantName { get; set; } =
        "Asistente ";

    /// <summary>
    /// Modelo opcional.
    /// Si está vacío, la API decide el modelo a utilizar.
    /// </summary>
    public string? Model { get; set; } = "";

    /// <summary>
    /// Número máximo de mensajes históricos que se enviarán a la API.
    /// </summary>
    public int MaxHistoryMessages { get; set; } = 6;

    /// <summary>
    /// Indica si la aplicación debe arrancar automáticamente
    /// al iniciar sesión en Windows.
    /// </summary>
    public bool StartWithWindows { get; set; } = false;

    /// <summary>
    /// Intervalo en segundos con el que el Desktop consulta
    /// si existen recordatorios vencidos pendientes de mostrar.
    /// </summary>
    public int ReminderPollingSeconds { get; set; } = 30;

    /// <summary>
    /// Endpoint de la API que devuelve recordatorios vencidos
    /// todavía no notificados al usuario.
    /// </summary>
    public string ApiDueRemindersUrl { get; set; } =
        "http://10.0.0.210:5055/api/reminders/due";

    /// <summary>
    /// Endpoint de la API que marca un recordatorio
    /// como ya mostrado al usuario.
    /// </summary>
    public string ApiMarkReminderNotifiedUrl { get; set; } =
        "http://10.0.0.210:5055/api/reminders/mark-notified";

    /// <summary>
    /// Endpoint para consultar acciones locales pendientes
    /// que el Desktop debe procesar.
    /// </summary>
    public string ApiPendingLocalActionsUrl { get; set; } =
        "http://10.0.0.210:5055/api/local-actions/pending";

    /// <summary>
    /// Endpoint para informar a la API del resultado
    /// de una acción local ya procesada.
    /// </summary>
    public string ApiCompleteLocalActionUrl { get; set; } =
        "http://10.0.0.210:5055/api/local-actions/complete";

    /// <summary>
    /// Intervalo en segundos para consultar acciones locales pendientes.
    /// </summary>
    public int LocalActionsPollingSeconds { get; set; } = 10;

    /// <summary>
    /// Lista blanca de programas que el Desktop permite cerrar.
    /// </summary>
    public List<AllowedProgramConfig> AllowedPrograms { get; set; } = [];

    /// <summary>
    /// Lista blanca de programas que el Desktop permite abrir.
    /// </summary>
    public List<AllowedOpenProgramConfig> AllowedOpenPrograms { get; set; } = [];

    /// <summary>
    /// Lista blanca de carpetas que el Desktop permite abrir.
    /// </summary>
    public List<AllowedFolderConfig> AllowedFolders { get; set; } = [];

    /// <summary>
    /// Configuración de la Drop Zone de impresión rápida.
    /// </summary>
    public QuickPrintConfig QuickPrint { get; set; } = new();

    /// <summary>
    /// Configuración del módulo de generación de carteles y etiquetas.
    /// </summary>
    public PosterGeneratorConfig PosterGenerator { get; set; } = new();

    public DesktopTaskConfig DesktopTasks { get; set; } = new();


    /// <summary>
    /// Carga config.json desde el directorio del ejecutable.
    /// Si el archivo no existe, lo crea con valores por defecto.
    /// </summary>
    public static AppConfig Load()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "config.json"
        );

        if (!File.Exists(path))
        {
            var defaultConfig = new AppConfig();

            var defaultJson = JsonSerializer.Serialize(
                defaultConfig,
                JsonOptions.Default
            );

            File.WriteAllText(
                path,
                defaultJson,
                Encoding.UTF8
            );

            return defaultConfig;
        }

        var json = File.ReadAllText(
            path,
            Encoding.UTF8
        );

        return JsonSerializer.Deserialize<AppConfig>(
                   json,
                   JsonOptions.Default
               )
               ?? new AppConfig();
    }

    /// <summary>
    /// Guarda la configuración actual en config.json.
    /// </summary>
    public void Save()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "config.json"
        );

        var json = JsonSerializer.Serialize(
            this,
            JsonOptions.Default
        );

        File.WriteAllText(
            path,
            json,
            Encoding.UTF8
        );
    }
}
