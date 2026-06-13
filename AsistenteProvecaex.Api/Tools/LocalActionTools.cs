using System.Text.Json;

/// <summary>
/// Herramienta que solicita al Desktop del usuario
/// cerrar un programa local.
///
/// La API no cierra procesos directamente.
/// Únicamente registra una acción pendiente que:
/// - queda asociada al usuario,
/// - queda asociada al equipo concreto,
/// - será recogida y procesada por el Desktop.
///
/// La validación final de seguridad se realiza en el Desktop
/// mediante la lista blanca allowedPrograms.
/// </summary>
public sealed class RequestCloseLocalProgramTool : IAssistantServerTool
{
    private readonly LocalActionRepository _repository;

    public RequestCloseLocalProgramTool(
        LocalActionRepository repository)
    {
        _repository = repository;
    }



    public string Name => "request_close_local_program";

    public OllamaToolDefinition Definition => new()
    {
        Type = "function",
        Function = new OllamaFunctionDefinition
        {
            Name = Name,
            Description =
                "Solicita cerrar un programa local en el equipo actual del usuario. " +
                "Usar cuando el usuario pida cerrar una aplicación, navegador o programa abierto. " +
                "El Desktop validará después si ese programa está autorizado para cerrarse.",
            Parameters = new OllamaFunctionParameters
            {
                Type = "object",
                Required = ["programName"],
                Properties = new()
                {
                    ["programName"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "Nombre del programa que el usuario desea cerrar. " +
                            "Debe conservar una forma comprensible, por ejemplo: " +
                            "'Bloc de notas', 'Microsoft Edge' o 'navegador Edge'."
                    }
                }
            }
        }
    };

    public async Task<ToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        AssistantToolExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        if (!executionContext.HasIdentifiedUser)
        {
            return ToolExecutionResult.Fail(
                "No se pudo identificar al usuario actual."
            );
        }

        if (string.IsNullOrWhiteSpace(
                executionContext.Client.MachineName))
        {
            return ToolExecutionResult.Fail(
                "No se pudo identificar el equipo actual del usuario."
            );
        }

        var programName = ToolArgumentReader.GetOptionalString(
            arguments,
            "programName"
        );

        if (string.IsNullOrWhiteSpace(programName))
        {
            return ToolExecutionResult.Fail(
                "No se ha indicado un programa válido para cerrar."
            );
        }

        var action = await _repository.CreateActionAsync(
            executionContext.OwnerKey,
            executionContext.Client.MachineName,
            actionType: "close_program",
            target: programName,
            cancellationToken
        );

        return ToolExecutionResult.Ok(
            $"Se ha enviado la orden para cerrar '{programName}' " +
            $"en el equipo {executionContext.Client.MachineName}. " +
            $"ID de acción {action.ShortId}."
        );
    }
}

/// <summary>
/// Herramienta que solicita al Desktop del usuario
/// abrir un programa local.
///
/// La API no abre aplicaciones directamente.
/// Únicamente registra una acción pendiente que:
/// - queda asociada al usuario,
/// - queda asociada al equipo concreto,
/// - será recogida y procesada por el Desktop.
///
/// La validación final de seguridad se realiza en el Desktop
/// mediante la lista blanca AllowedOpenPrograms.
/// </summary>
public sealed class RequestOpenLocalProgramTool : IAssistantServerTool
{
    private readonly LocalActionRepository _repository;

    public RequestOpenLocalProgramTool(
        LocalActionRepository repository)
    {
        _repository = repository;
    }

    public string Name => "request_open_local_program";

    public OllamaToolDefinition Definition => new()
    {
        Type = "function",
        Function = new OllamaFunctionDefinition
        {
            Name = Name,
            Description =
                "Solicita abrir un programa local en el equipo actual del usuario. " +
                "Usar cuando el usuario pida abrir, iniciar o ejecutar una aplicación o programa. " +
                "El Desktop validará después si ese programa está autorizado para abrirse.",
            Parameters = new OllamaFunctionParameters
            {
                Type = "object",
                Required = ["programName"],
                Properties = new()
                {
                    ["programName"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "Nombre del programa que el usuario desea abrir. " +
                            "Debe conservar una forma comprensible, por ejemplo: " +
                            "'Bloc de notas', 'Microsoft Edge' o 'Excel'."
                    }
                }
            }
        }
    };

    public async Task<ToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        AssistantToolExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        if (!executionContext.HasIdentifiedUser)
        {
            return ToolExecutionResult.Fail(
                "No se pudo identificar al usuario actual."
            );
        }

        if (string.IsNullOrWhiteSpace(
                executionContext.Client.MachineName))
        {
            return ToolExecutionResult.Fail(
                "No se pudo identificar el equipo actual del usuario."
            );
        }

        var programName = ToolArgumentReader.GetOptionalString(
            arguments,
            "programName"
        );

        if (string.IsNullOrWhiteSpace(programName))
        {
            return ToolExecutionResult.Fail(
                "No se ha indicado un programa válido para abrir."
            );
        }

        var action = await _repository.CreateActionAsync(
            executionContext.OwnerKey,
            executionContext.Client.MachineName,
            actionType: "open_program",
            target: programName,
            cancellationToken
        );

        return ToolExecutionResult.Ok(
            $"Se ha enviado la orden para abrir '{programName}' " +
            $"en el equipo {executionContext.Client.MachineName}. " +
            $"ID de acción {action.ShortId}."
        );
    }
}

/// <summary>
/// Herramienta que solicita al Desktop del usuario
/// abrir una carpeta local o compartida previamente autorizada.
///
/// La API no abre rutas directamente.
/// Únicamente registra una acción pendiente que:
/// - queda asociada al usuario,
/// - queda asociada al equipo concreto,
/// - será recogida y procesada por el Desktop.
///
/// La validación final de seguridad se realiza en el Desktop
/// mediante la lista blanca AllowedFolders.
/// </summary>
public sealed class RequestOpenLocalFolderTool : IAssistantServerTool
{
    private readonly LocalActionRepository _repository;

    public RequestOpenLocalFolderTool(
        LocalActionRepository repository)
    {
        _repository = repository;
    }

    public string Name => "request_open_local_folder";

    public OllamaToolDefinition Definition => new()
    {
        Type = "function",
        Function = new OllamaFunctionDefinition
        {
            Name = Name,
            Description =
                "Solicita abrir una carpeta autorizada en el equipo actual del usuario. " +
                "Usar cuando el usuario pida abrir una carpeta, una carpeta compartida, " +
                "una ruta permitida o una ubicación de trabajo configurada en su Desktop.",
            Parameters = new OllamaFunctionParameters
            {
                Type = "object",
                Required = ["folderName"],
                Properties = new()
                {
                    ["folderName"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "Nombre de la carpeta que el usuario desea abrir. " +
                            "Debe conservar una forma comprensible, por ejemplo: " +
                            "'Mi carpeta compartida' o 'carpeta compartida'."
                    }
                }
            }
        }
    };

    public async Task<ToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        AssistantToolExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        if (!executionContext.HasIdentifiedUser)
        {
            return ToolExecutionResult.Fail(
                "No se pudo identificar al usuario actual."
            );
        }

        if (string.IsNullOrWhiteSpace(
                executionContext.Client.MachineName))
        {
            return ToolExecutionResult.Fail(
                "No se pudo identificar el equipo actual del usuario."
            );
        }

        var folderName = ToolArgumentReader.GetOptionalString(
            arguments,
            "folderName"
        );

        if (string.IsNullOrWhiteSpace(folderName))
        {
            return ToolExecutionResult.Fail(
                "No se ha indicado una carpeta válida para abrir."
            );
        }

        var action = await _repository.CreateActionAsync(
            executionContext.OwnerKey,
            executionContext.Client.MachineName,
            actionType: "open_folder",
            target: folderName,
            cancellationToken
        );

        return ToolExecutionResult.Ok(
            $"Se ha enviado la orden para abrir la carpeta '{folderName}' " +
            $"en el equipo {executionContext.Client.MachineName}. " +
            $"ID de acción {action.ShortId}."
        );
    }
}