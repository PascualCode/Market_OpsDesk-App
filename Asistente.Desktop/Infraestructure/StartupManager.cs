using Microsoft.Win32;

namespace Asistente.Desktop.Infrastructure;

/// <summary>
/// Gestiona el inicio automático de la aplicación con Windows.
///
/// Usa la clave de registro HKCU, por lo que no requiere permisos
/// de administrador y afecta solo al usuario actual.
/// </summary>
public static class StartupManager
{
    private const string AppName = "Asistente";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Comprueba si la aplicación ya está configurada para iniciar con Windows.
    /// </summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);

        var value = key?.GetValue(AppName)?.ToString();

        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// Activa o desactiva el inicio automático.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
                       ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
        {
            var exePath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(exePath))
                return;

            key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }
}