using Asistente.Desktop.Configuration;
using Asistente.Desktop.Models;
using System.Diagnostics;
using System.IO;

namespace Asistente.Desktop.Services;

/// <summary>
/// Ejecuta acciones locales autorizadas en el equipo del usuario.
///
/// Responsabilidades:
/// - Cerrar programas permitidos.
/// - Abrir programas permitidos.
/// - Abrir carpetas permitidas.
///
/// La seguridad se basa en las listas blancas
/// configuradas en config.json.
/// </summary>
public sealed class LocalActionExecutionService
{
    private readonly AppConfig _config;

    public LocalActionExecutionService(
        AppConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Procesa una acción local pendiente y devuelve
    /// el resultado real de su ejecución.
    /// </summary>
    public LocalActionExecutionResult Execute(
        PendingLocalActionItem action)
    {
        return action.ActionType switch
        {
            "close_program" => ExecuteCloseProgramAction(
                action.Target
            ),

            "open_program" => ExecuteOpenProgramAction(
                action.Target
            ),

            "open_folder" => ExecuteOpenFolderAction(
                action.Target
            ),

            _ => new LocalActionExecutionResult
            {
                Success = false,
                Message =
                    $"Tipo de acción local no soportado por el Desktop: {action.ActionType}."
            }
        };
    }

    /// <summary>
    /// Ejecuta localmente una orden de cierre de programa.
    ///
    /// Solo se cerrarán procesos que:
    /// - Coincidan con un programa permitido en config.json.
    /// - Estén activos en el equipo.
    /// </summary>
    private LocalActionExecutionResult ExecuteCloseProgramAction(
        string requestedTarget)
    {
        if (string.IsNullOrWhiteSpace(requestedTarget))
        {
            return new LocalActionExecutionResult
            {
                Success = false,
                Message = "No se ha recibido un programa válido para cerrar."
            };
        }

        var allowedProgram = FindAllowedProgram(requestedTarget);

        if (allowedProgram is null)
        {
            return new LocalActionExecutionResult
            {
                Success = false,
                Message =
                    $"El programa solicitado '{requestedTarget}' no está autorizado para cerrarse desde el asistente."
            };
        }

        var totalClosed = 0;
        var totalFound = 0;

        foreach (var processName in allowedProgram.ProcessNames)
        {
            if (string.IsNullOrWhiteSpace(processName))
                continue;

            var normalizedProcessName =
                Path.GetFileNameWithoutExtension(
                    processName.Trim()
                );

            var processes = Process.GetProcessesByName(
                normalizedProcessName
            );

            totalFound += processes.Length;

            foreach (var process in processes)
            {
                try
                {
                    var closedGracefully = false;

                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        closedGracefully = process.CloseMainWindow();

                        if (closedGracefully)
                        {
                            process.WaitForExit(3000);
                        }
                    }

                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(3000);
                    }

                    if (process.HasExited)
                    {
                        totalClosed++;
                    }
                }
                catch
                {
                    // Seguimos intentando cerrar otros procesos
                    // del mismo programa.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        if (totalFound == 0)
        {
            return new LocalActionExecutionResult
            {
                Success = false,
                Message =
                    $"{allowedProgram.DisplayName} no estaba abierto en el equipo."
            };
        }

        if (totalClosed == 0)
        {
            return new LocalActionExecutionResult
            {
                Success = false,
                Message =
                    $"No se ha podido cerrar {allowedProgram.DisplayName}."
            };
        }

        return new LocalActionExecutionResult
        {
            Success = true,
            Message =
                $"Se ha cerrado {allowedProgram.DisplayName}. Procesos cerrados: {totalClosed}."
        };
    }

    /// <summary>
    /// Ejecuta localmente una orden de apertura de programa.
    ///
    /// Solo se abrirán programas incluidos en la lista blanca
    /// AllowedOpenPrograms del config.json local.
    /// </summary>
    private LocalActionExecutionResult ExecuteOpenProgramAction(
        string requestedTarget)
    {
        if (string.IsNullOrWhiteSpace(requestedTarget))
        {
            return new LocalActionExecutionResult
            {
                Success = false,
                Message = "No se ha recibido un programa válido para abrir."
            };
        }

        var allowedProgram =
            FindAllowedOpenProgram(requestedTarget);

        if (allowedProgram is null)
        {
            return new LocalActionExecutionResult
            {
                Success = false,
                Message =
                    $"El programa solicitado '{requestedTarget}' no está autorizado para abrirse desde el asistente."
            };
        }

        if (string.IsNullOrWhiteSpace(allowedProgram.ExecutablePath))
        {
            return new LocalActionExecutionResult
            {
                Success = false,
                Message =
                    $"El programa '{allowedProgram.DisplayName}' no tiene una ruta de ejecución configurada."
            };
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = allowedProgram.ExecutablePath,
                UseShellExecute = true
            });

            return new LocalActionExecutionResult
            {
                Success = true,
                Message =
                    $"Se ha abierto {allowedProgram.DisplayName} correctamente."
            };
        }
        catch (Exception ex)
        {
            return new LocalActionExecutionResult
            {
                Success = false,
                Message =
                    $"No se ha podido abrir {allowedProgram.DisplayName}: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Ejecuta localmente una orden de apertura de carpeta.
    ///
    /// Solo se abrirán carpetas incluidas en la lista blanca
    /// AllowedFolders del config.json local.
    /// </summary>
    private LocalActionExecutionResult ExecuteOpenFolderAction(
        string requestedTarget)
    {
        if (string.IsNullOrWhiteSpace(requestedTarget))
        {
            return new LocalActionExecutionResult
            {
                Success = false,
                Message = "No se ha recibido una carpeta válida para abrir."
            };
        }

        var allowedFolder =
            FindAllowedFolder(requestedTarget);

        if (allowedFolder is null)
        {
            return new LocalActionExecutionResult
            {
                Success = false,
                Message =
                    $"La carpeta solicitada '{requestedTarget}' no está autorizada para abrirse desde el asistente."
            };
        }

        if (string.IsNullOrWhiteSpace(allowedFolder.FolderPath))
        {
            return new LocalActionExecutionResult
            {
                Success = false,
                Message =
                    $"La carpeta '{allowedFolder.DisplayName}' no tiene una ruta configurada."
            };
        }

        if (!Directory.Exists(allowedFolder.FolderPath))
        {
            return new LocalActionExecutionResult
            {
                Success = false,
                Message =
                    $"La ruta configurada para '{allowedFolder.DisplayName}' no existe o no es accesible: {allowedFolder.FolderPath}"
            };
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = allowedFolder.FolderPath,
                UseShellExecute = true
            });

            return new LocalActionExecutionResult
            {
                Success = true,
                Message =
                    $"Se ha abierto la carpeta {allowedFolder.DisplayName} correctamente."
            };
        }
        catch (Exception ex)
        {
            return new LocalActionExecutionResult
            {
                Success = false,
                Message =
                    $"No se ha podido abrir la carpeta {allowedFolder.DisplayName}: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Busca en la lista blanca local un programa
    /// autorizado para cerrar.
    /// </summary>
    private AllowedProgramConfig? FindAllowedProgram(
        string requestedTarget)
    {
        var normalizedRequestedTarget =
            NormalizeLookupText(requestedTarget);

        return _config.AllowedPrograms
            .FirstOrDefault(program =>
            {
                if (NormalizeLookupText(program.DisplayName) ==
                    normalizedRequestedTarget)
                {
                    return true;
                }

                return program.Aliases.Any(alias =>
                    NormalizeLookupText(alias) ==
                    normalizedRequestedTarget
                );
            });
    }

    /// <summary>
    /// Busca en la lista blanca local un programa
    /// autorizado para abrir.
    /// </summary>
    private AllowedOpenProgramConfig? FindAllowedOpenProgram(
        string requestedTarget)
    {
        var normalizedRequestedTarget =
            NormalizeLookupText(requestedTarget);

        return _config.AllowedOpenPrograms
            .FirstOrDefault(program =>
            {
                if (NormalizeLookupText(program.DisplayName) ==
                    normalizedRequestedTarget)
                {
                    return true;
                }

                return program.Aliases.Any(alias =>
                    NormalizeLookupText(alias) ==
                    normalizedRequestedTarget
                );
            });
    }

    /// <summary>
    /// Busca en la lista blanca local una carpeta
    /// autorizada para abrir.
    /// </summary>
    private AllowedFolderConfig? FindAllowedFolder(
        string requestedTarget)
    {
        var normalizedRequestedTarget =
            NormalizeLookupText(requestedTarget);

        return _config.AllowedFolders
            .FirstOrDefault(folder =>
            {
                if (NormalizeLookupText(folder.DisplayName) ==
                    normalizedRequestedTarget)
                {
                    return true;
                }

                return folder.Aliases.Any(alias =>
                    NormalizeLookupText(alias) ==
                    normalizedRequestedTarget
                );
            });
    }

    /// <summary>
    /// Normalización sencilla para comparar nombres y alias
    /// sin depender de mayúsculas/minúsculas ni espacios sobrantes.
    /// </summary>
    private static string NormalizeLookupText(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return string.Join(
            ' ',
            value
                .Trim()
                .ToLowerInvariant()
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries
                )
        );
    }
}