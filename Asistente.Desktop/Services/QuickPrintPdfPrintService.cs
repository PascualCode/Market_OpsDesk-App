using Asistente.Desktop.Configuration;
using Asistente.Desktop.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace Asistente.Desktop.Services;

/// <summary>
/// Servicio que envía PDFs a impresión rápida
/// utilizando Adobe Acrobat Reader.
///
/// Flujo:
/// - Localiza el ejecutable de Adobe Reader.
/// - Valida impresora, driver y puerto.
/// - Ejecuta Adobe Reader con /t.
/// </summary>
public sealed class QuickPrintPdfPrintService
{
    private readonly QuickPrintConfig _config;

    public QuickPrintPdfPrintService(
        QuickPrintConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Intenta enviar un PDF a la impresora seleccionada.
    /// </summary>
    public QuickPrintExecutionResult PrintPdf(
        QuickPrintSelectedFile selectedFile,
        QuickPrintPrinterItem printer)
    {
        if (selectedFile is null ||
            string.IsNullOrWhiteSpace(selectedFile.FullPath))
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message = "No hay ningún PDF válido preparado para imprimir."
            };
        }

        if (!File.Exists(selectedFile.FullPath))
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message = "El PDF seleccionado ya no existe o no es accesible."
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

        if (string.IsNullOrWhiteSpace(printer.DriverName) ||
            string.IsNullOrWhiteSpace(printer.PortName))
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message =
                    $"No se han podido obtener el controlador o el puerto de la impresora '{printer.DisplayName}'."
            };
        }

        var adobeExecutablePath =
            ResolveAdobeReaderExecutablePath();

        if (string.IsNullOrWhiteSpace(adobeExecutablePath))
        {
            return new QuickPrintExecutionResult
            {
                Success = false,
                Message =
                    "No se ha localizado Adobe Acrobat Reader en este equipo. " +
                    "Puedes fijar su ruta manualmente en config.json."
            };
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = adobeExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            startInfo.ArgumentList.Add("/t");
            startInfo.ArgumentList.Add(selectedFile.FullPath);
            startInfo.ArgumentList.Add(printer.PrinterName);
            startInfo.ArgumentList.Add(printer.DriverName);
            startInfo.ArgumentList.Add(printer.PortName);

            var existingAdobeProcessIds = GetAdobeProcessIds();

            Process.Start(startInfo);

            if (_config.CloseAdobeAfterPrint)
            {
                var delaySeconds = Math.Clamp(
                    _config.CloseAdobeAfterPrintSeconds,
                    5,
                    120
                );

                ScheduleCloseNewAdobeProcesses(
                    existingAdobeProcessIds,
                    TimeSpan.FromSeconds(delaySeconds)
                );
            }

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
                    $"No se ha podido lanzar Adobe Acrobat Reader para imprimir: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Resuelve la ruta de Adobe Acrobat Reader.
    ///
    /// Orden:
    /// 1. Ruta manual configurada en config.json.
    /// 2. Registro App Paths de Windows.
    /// 3. Rutas habituales de instalación.
    /// </summary>
    private string? ResolveAdobeReaderExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(
                _config.AdobeReaderExecutablePath) &&
            File.Exists(_config.AdobeReaderExecutablePath))
        {
            return _config.AdobeReaderExecutablePath;
        }

        foreach (var registryCandidate in GetRegistryCandidates())
        {
            if (File.Exists(registryCandidate))
            {
                return registryCandidate;
            }
        }

        foreach (var fileCandidate in GetCommonFileCandidates())
        {
            if (File.Exists(fileCandidate))
            {
                return fileCandidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Busca Adobe Reader / Acrobat en App Paths del registro.
    /// </summary>
    private static IEnumerable<string> GetRegistryCandidates()
    {
        var executableNames = new[]
        {
            "AcroRd32.exe",
            "Acrobat.exe"
        };

        var registryViews = new[]
        {
            RegistryView.Registry64,
            RegistryView.Registry32
        };

        foreach (var registryView in registryViews)
        {
            using var localMachine =
                RegistryKey.OpenBaseKey(
                    RegistryHive.LocalMachine,
                    registryView
                );

            foreach (var executableName in executableNames)
            {
                using var key = localMachine.OpenSubKey(
                    $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executableName}"
                );

                var candidate =
                    key?.GetValue(null)?.ToString();

                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    yield return candidate;
                }
            }
        }

        foreach (var executableName in executableNames)
        {
            using var currentUserKey =
                Registry.CurrentUser.OpenSubKey(
                    $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executableName}"
                );

            var candidate =
                currentUserKey?.GetValue(null)?.ToString();

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                yield return candidate;
            }
        }
    }

    /// <summary>
    /// Rutas habituales de Adobe Reader y Acrobat en Windows.
    /// </summary>
    private static IEnumerable<string> GetCommonFileCandidates()
    {
        var programFiles =
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles
            );

        var programFilesX86 =
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86
            );

        yield return Path.Combine(
            programFiles,
            "Adobe",
            "Acrobat DC",
            "Acrobat",
            "Acrobat.exe"
        );

        yield return Path.Combine(
            programFilesX86,
            "Adobe",
            "Acrobat DC",
            "Acrobat",
            "Acrobat.exe"
        );

        yield return Path.Combine(
            programFiles,
            "Adobe",
            "Acrobat Reader DC",
            "Reader",
            "AcroRd32.exe"
        );

        yield return Path.Combine(
            programFilesX86,
            "Adobe",
            "Acrobat Reader DC",
            "Reader",
            "AcroRd32.exe"
        );
    }

    /// <summary>
    /// Obtiene los procesos actuales de Adobe Reader / Acrobat.
    /// Se usa para no cerrar procesos que el usuario ya tenía abiertos.
    /// </summary>
    private static HashSet<int> GetAdobeProcessIds()
    {
        var result = new HashSet<int>();

        var processNames = new[]
        {
        "AcroRd32",
        "Acrobat"
    };

        foreach (var processName in processNames)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        result.Add(process.Id);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                // Ignoramos errores de lectura de procesos.
            }
        }

        return result;
    }

    /// <summary>
    /// Cierra de forma diferida solo los procesos nuevos de Adobe
    /// creados después de lanzar la impresión.
    /// </summary>
    private static void ScheduleCloseNewAdobeProcesses(
        HashSet<int> existingAdobeProcessIds,
        TimeSpan delay)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay);

                var processNames = new[]
                {
                "AcroRd32",
                "Acrobat"
            };

                foreach (var processName in processNames)
                {
                    foreach (var process in Process.GetProcessesByName(processName))
                    {
                        try
                        {
                            if (existingAdobeProcessIds.Contains(process.Id))
                                continue;

                            if (process.HasExited)
                                continue;

                            if (process.MainWindowHandle != IntPtr.Zero)
                            {
                                process.CloseMainWindow();

                                if (process.WaitForExit(5000))
                                    continue;
                            }

                            if (!process.HasExited)
                            {
                                process.Kill(entireProcessTree: true);
                                process.WaitForExit(5000);
                            }
                        }
                        catch
                        {
                            // No bloqueamos la aplicación si no se puede cerrar Adobe.
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
            }
            catch
            {
                // No bloqueamos la aplicación por fallos al cerrar Adobe.
            }
        });
    }
}
