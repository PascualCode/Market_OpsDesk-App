using System.Globalization;
using System.Text.Json;
using System.Text;

/// <summary>
/// Herramienta que permite al asistente crear una tarea
/// personal para el usuario actual.
/// </summary>
public sealed class CreateTaskTool : IAssistantServerTool
{
    private readonly PlanningRepository _repository;

    public CreateTaskTool(PlanningRepository repository)
    {
        _repository = repository;
    }

    public string Name => "create_task";

    public OllamaToolDefinition Definition => new()
    {
        Type = "function",
        Function = new OllamaFunctionDefinition
        {
            Name = Name,
            Description =
                "Crea una tarea personal pendiente para el usuario actual.",
            Parameters = new OllamaFunctionParameters
            {
                Type = "object",
                Required = ["title"],
                Properties = new()
                {
                    ["title"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "Título breve y claro de la tarea."
                    },

                    ["notes"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "Notas opcionales sobre la tarea."
                    },

                    ["dueAt"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "Fecha y hora exactas de la tarea en formato ISO 8601 con zona horaria. " +
                            "Usar solo si el usuario ha indicado una hora concreta."
                    },

                    ["dueDateLocal"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "Fecha local de la tarea sin hora concreta, en formato yyyy-MM-dd. " +
                            "Usar cuando el usuario indique un día como hoy, mañana o el viernes, " +
                            "pero no diga una hora exacta. No usar a la vez que dueAt."
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

        var title = ToolArgumentReader.GetOptionalString(
            arguments,
            "title"
        );

        if (string.IsNullOrWhiteSpace(title))
        {
            return ToolExecutionResult.Fail(
                "No se ha recibido un título válido para la tarea."
            );
        }

        var notes = ToolArgumentReader.GetOptionalString(
            arguments,
            "notes"
        );

        var dueAtRaw = ToolArgumentReader.GetOptionalString(
            arguments,
            "dueAt"
        );

        var dueDateLocalRaw = ToolArgumentReader.GetOptionalString(
            arguments,
            "dueDateLocal"
        );

        DateTimeOffset? dueAtUtc = null;
        string? dueDateLocal = null;

        if (!string.IsNullOrWhiteSpace(dueAtRaw))
        {
            if (!DateTimeOffset.TryParse(
                    dueAtRaw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var parsedDueAt))
            {
                return ToolExecutionResult.Fail(
                    "La fecha límite de la tarea no tiene un formato válido."
                );
            }

            dueAtUtc = parsedDueAt.ToUniversalTime();
        }

        if (dueAtUtc is null &&
    !string.IsNullOrWhiteSpace(dueDateLocalRaw))
        {
            if (!DateOnly.TryParseExact(
                    dueDateLocalRaw,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            {
                return ToolExecutionResult.Fail(
                    "La fecha de la tarea sin hora no tiene un formato válido."
                );
            }

            dueDateLocal = dueDateLocalRaw;
        }

        var task = await _repository.CreateTaskAsync(
            executionContext.OwnerKey,
            title,
            notes,
            dueAtUtc,
            dueDateLocal,
            cancellationToken
        );

        string dueText;

        if (task.DueAtUtc is not null)
        {
            dueText =
                $"con fecha límite {task.DueAtUtc.Value.ToLocalTime():dd/MM/yyyy HH:mm}";
        }
        else if (!string.IsNullOrWhiteSpace(task.DueDateLocal) &&
                 DateOnly.TryParseExact(
                     task.DueDateLocal,
                     "yyyy-MM-dd",
                     CultureInfo.InvariantCulture,
                     DateTimeStyles.None,
                     out var dueDate))
        {
            dueText =
                $"para el día {dueDate:dd/MM/yyyy}, sin hora concreta";
        }
        else
        {
            dueText = "sin fecha límite";
        }

        return ToolExecutionResult.Ok(
            $"Tarea creada correctamente. " +
            $"ID {task.ShortId}. " +
            $"Título: {task.Title}. " +
            $"{dueText}."
        );
    }
}

/// <summary>
/// Herramienta que permite al asistente consultar
/// las tareas del usuario actual.
/// </summary>
public sealed class ListTasksTool : IAssistantServerTool
{
    private readonly PlanningRepository _repository;

    public ListTasksTool(PlanningRepository repository)
    {
        _repository = repository;
    }

    public string Name => "list_tasks";

    public OllamaToolDefinition Definition => new()
    {
        Type = "function",
        Function = new OllamaFunctionDefinition
        {
            Name = Name,
            Description =
                "Lista tareas del usuario actual. " +
                "Permite consultar tareas pendientes, de hoy, completadas o todas.",
            Parameters = new OllamaFunctionParameters
            {
                Type = "object",
                Required = [],
                Properties = new()
                {
                    ["scope"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "Ámbito de búsqueda: " +
                            "pending, today, completed o all. " +
                            "Si no se indica, usar pending."
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

        var scope = ToolArgumentReader.GetOptionalString(
            arguments,
            "scope"
        ) ?? "pending";

        var tasks = await _repository.ListTasksAsync(
            executionContext.OwnerKey,
            scope,
            cancellationToken
        );

        if (tasks.Count == 0)
        {
            return ToolExecutionResult.Ok(
                "No hay tareas que mostrar para ese criterio."
            );
        }

        var sb = new StringBuilder();

        sb.AppendLine("Tareas encontradas:");

        foreach (var task in tasks)
        {
            string dueText;

            if (task.DueAtUtc is not null)
            {
                dueText = task.DueAtUtc.Value
                    .ToLocalTime()
                    .ToString("dd/MM/yyyy HH:mm");
            }
            else if (!string.IsNullOrWhiteSpace(task.DueDateLocal) &&
                     DateOnly.TryParseExact(
                         task.DueDateLocal,
                         "yyyy-MM-dd",
                         CultureInfo.InvariantCulture,
                         DateTimeStyles.None,
                         out var dueDate))
            {
                dueText = $"{dueDate:dd/MM/yyyy}, sin hora concreta";
            }
            else
            {
                dueText = "sin fecha";
            }

            sb.AppendLine(
                $"- ID {task.ShortId}: " +
                $"{task.Title} | " +
                $"Estado: {task.Status} | " +
                $"Fecha: {dueText}"
            );
        }

        return ToolExecutionResult.Ok(
            sb.ToString().Trim()
        );
    }
}

/// <summary>
/// Herramienta que permite marcar como completada
/// una tarea pendiente del usuario actual.
/// </summary>
public sealed class CompleteTaskTool : IAssistantServerTool
{
    private readonly PlanningRepository _repository;

    public CompleteTaskTool(PlanningRepository repository)
    {
        _repository = repository;
    }

    public string Name => "complete_task";

    public OllamaToolDefinition Definition => new()
    {
        Type = "function",
        Function = new OllamaFunctionDefinition
        {
            Name = Name,
            Description =
                "Marca como completada una tarea pendiente del usuario actual. " +
                "Puede recibir un ID corto o una parte del título.",
            Parameters = new OllamaFunctionParameters
            {
                Type = "object",
                Required = ["taskIdOrText"],
                Properties = new()
                {
                    ["taskIdOrText"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "ID de la tarea o texto suficiente para identificarla."
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

        var taskIdOrText = ToolArgumentReader.GetOptionalString(
            arguments,
            "taskIdOrText"
        );

        if (string.IsNullOrWhiteSpace(taskIdOrText))
        {
            return ToolExecutionResult.Fail(
                "No se ha indicado qué tarea debe completarse."
            );
        }

        var matches = await _repository.FindPendingTasksAsync(
            executionContext.OwnerKey,
            taskIdOrText,
            cancellationToken
        );

        if (matches.Count == 0)
        {
            return ToolExecutionResult.Fail(
                "No se ha encontrado ninguna tarea pendiente que coincida."
            );
        }

        if (matches.Count > 1)
        {
            var sb = new StringBuilder();

            sb.AppendLine(
                "He encontrado varias tareas posibles. " +
                "Indica una con más precisión:"
            );

            foreach (var task in matches)
            {
                sb.AppendLine(
                    $"- ID {task.ShortId}: {task.Title}"
                );
            }

            return ToolExecutionResult.Fail(
                sb.ToString().Trim()
            );
        }

        var selectedTask = matches[0];

        await _repository.MarkTaskCompletedAsync(
            selectedTask.Id,
            executionContext.OwnerKey,
            cancellationToken
        );

        return ToolExecutionResult.Ok(
            $"Tarea completada correctamente: {selectedTask.Title}."
        );
    }
}

/// <summary>
/// Herramienta que permite eliminar una tarea pendiente
/// del usuario actual.
///
/// Puede identificar la tarea por:
/// - ID corto o completo.
/// - Parte del título.
/// </summary>
public sealed class DeleteTaskTool : IAssistantServerTool
{
    private readonly PlanningRepository _repository;

    public DeleteTaskTool(PlanningRepository repository)
    {
        _repository = repository;
    }

    public string Name => "delete_task";

    public OllamaToolDefinition Definition => new()
    {
        Type = "function",
        Function = new OllamaFunctionDefinition
        {
            Name = Name,
            Description =
                "Elimina una tarea pendiente del usuario actual. " +
                "Puede recibir un ID corto o una parte del título.",
            Parameters = new OllamaFunctionParameters
            {
                Type = "object",
                Required = ["taskIdOrText"],
                Properties = new()
                {
                    ["taskIdOrText"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "ID de la tarea o texto suficiente para identificarla."
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

        var taskIdOrText = ToolArgumentReader.GetOptionalString(
            arguments,
            "taskIdOrText"
        );

        if (string.IsNullOrWhiteSpace(taskIdOrText))
        {
            return ToolExecutionResult.Fail(
                "No se ha indicado qué tarea debe eliminarse."
            );
        }

        var matches = await _repository.FindPendingTasksAsync(
            executionContext.OwnerKey,
            taskIdOrText,
            cancellationToken
        );

        if (matches.Count == 0)
        {
            return ToolExecutionResult.Fail(
                "No se ha encontrado ninguna tarea pendiente que coincida."
            );
        }

        if (matches.Count > 1)
        {
            var sb = new StringBuilder();

            sb.AppendLine(
                "He encontrado varias tareas pendientes posibles. " +
                "Indica una con más precisión:"
            );

            foreach (var task in matches)
            {
                sb.AppendLine(
                    $"- ID {task.ShortId}: {task.Title}"
                );
            }

            return ToolExecutionResult.Fail(
                sb.ToString().Trim()
            );
        }

        var selectedTask = matches[0];

        await _repository.DeleteTaskAsync(
            selectedTask.Id,
            executionContext.OwnerKey,
            cancellationToken
        );

        return ToolExecutionResult.Ok(
            $"Tarea eliminada correctamente: {selectedTask.Title}."
        );
    }
}

/// <summary>
/// Herramienta que permite eliminar todas las tareas
/// del usuario actual.
///
/// Incluye:
/// - Tareas pendientes.
/// - Tareas completadas.
/// </summary>
public sealed class DeleteAllTasksTool : IAssistantServerTool
{
    private readonly PlanningRepository _repository;

    public DeleteAllTasksTool(PlanningRepository repository)
    {
        _repository = repository;
    }

    public string Name => "delete_all_tasks";

    public OllamaToolDefinition Definition => new()
    {
        Type = "function",
        Function = new OllamaFunctionDefinition
        {
            Name = Name,
            Description =
                "Elimina todas las tareas del usuario actual, tanto pendientes como completadas. " +
                "Debe usarse solo cuando el usuario pida de forma clara borrar o eliminar todas sus tareas.",
            Parameters = new OllamaFunctionParameters
            {
                Type = "object",
                Required = [],
                Properties = new()
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

        var deletedCount = await _repository.DeleteAllTasksAsync(
            executionContext.OwnerKey,
            cancellationToken
        );

        if (deletedCount == 0)
        {
            return ToolExecutionResult.Ok(
                "No había tareas que eliminar."
            );
        }

        return ToolExecutionResult.Ok(
            $"Se han eliminado correctamente {deletedCount} tarea(s) del usuario."
        );
    }
}

/// <summary>
/// Herramienta que permite al asistente crear un recordatorio,
/// cita o reunión para el usuario actual.
/// </summary>
public sealed class CreateReminderTool : IAssistantServerTool
{
    private readonly PlanningRepository _repository;

    public CreateReminderTool(PlanningRepository repository)
    {
        _repository = repository;
    }

    public string Name => "create_reminder";

    public OllamaToolDefinition Definition => new()
    {
        Type = "function",
        Function = new OllamaFunctionDefinition
        {
            Name = Name,
            Description =
                "Crea un recordatorio, cita o reunión para el usuario actual. " +
                "Debe usarse cuando el usuario pida que se le recuerde algo " +
                "en una fecha y hora concretas.",
            Parameters = new OllamaFunctionParameters
            {
                Type = "object",
                Required = ["title", "remindAt"],
                Properties = new()
                {
                    ["title"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "Título breve y claro del recordatorio."
                    },

                    ["notes"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "Notas opcionales sobre el recordatorio."
                    },

                    ["remindAt"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "Fecha y hora exactas del recordatorio en formato ISO 8601 con zona horaria. " +
                            "Ejemplo: 2026-05-15T10:30:00+02:00."
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

        var title = ToolArgumentReader.GetOptionalString(
            arguments,
            "title"
        );

        var notes = ToolArgumentReader.GetOptionalString(
            arguments,
            "notes"
        );

        var remindAtRaw = ToolArgumentReader.GetOptionalString(
            arguments,
            "remindAt"
        );

        if (string.IsNullOrWhiteSpace(title))
        {
            return ToolExecutionResult.Fail(
                "No se ha recibido un título válido para el recordatorio."
            );
        }

        if (string.IsNullOrWhiteSpace(remindAtRaw))
        {
            return ToolExecutionResult.Fail(
                "No se ha recibido una fecha y hora válidas para el recordatorio."
            );
        }

        if (!DateTimeOffset.TryParse(
                remindAtRaw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var remindAt))
        {
            return ToolExecutionResult.Fail(
                "La fecha del recordatorio no tiene un formato válido."
            );
        }

        var reminder = await _repository.CreateReminderAsync(
            executionContext.OwnerKey,
            title,
            notes,
            remindAt.ToUniversalTime(),
            cancellationToken
        );

        return ToolExecutionResult.Ok(
            $"Recordatorio creado correctamente. " +
            $"ID {reminder.ShortId}. " +
            $"Título: {reminder.Title}. " +
            $"Aviso previsto para " +
            $"{reminder.RemindAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}."
        );
    }
}

/// <summary>
/// Herramienta que permite al asistente listar
/// los recordatorios del usuario actual.
/// </summary>
public sealed class ListRemindersTool : IAssistantServerTool
{
    private readonly PlanningRepository _repository;

    public ListRemindersTool(PlanningRepository repository)
    {
        _repository = repository;
    }

    public string Name => "list_reminders";

    public OllamaToolDefinition Definition => new()
    {
        Type = "function",
        Function = new OllamaFunctionDefinition
        {
            Name = Name,
            Description =
                "Lista recordatorios del usuario actual. " +
                "Permite consultar próximos recordatorios, " +
                "recordatorios de hoy o todos los activos.",
            Parameters = new OllamaFunctionParameters
            {
                Type = "object",
                Required = [],
                Properties = new()
                {
                    ["scope"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "Ámbito de búsqueda: " +
                            "upcoming, today o all. " +
                            "Si no se indica, usar upcoming."
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

        var scope = ToolArgumentReader.GetOptionalString(
            arguments,
            "scope"
        ) ?? "upcoming";

        var reminders = await _repository.ListRemindersAsync(
            executionContext.OwnerKey,
            scope,
            cancellationToken
        );

        if (reminders.Count == 0)
        {
            return ToolExecutionResult.Ok(
                "No hay recordatorios que mostrar para ese criterio."
            );
        }

        var sb = new StringBuilder();

        sb.AppendLine("Recordatorios encontrados:");

        foreach (var reminder in reminders)
        {
            sb.AppendLine(
                $"- ID {reminder.ShortId}: " +
                $"{reminder.Title} | " +
                $"Fecha: {reminder.RemindAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}"
            );
        }

        return ToolExecutionResult.Ok(
            sb.ToString().Trim()
        );
    }
}

/// <summary>
/// Herramienta que permite eliminar o descartar
/// un recordatorio activo del usuario actual.
///
/// Puede identificar el recordatorio por:
/// - ID corto o completo.
/// - Parte del título.
/// </summary>
public sealed class DeleteReminderTool : IAssistantServerTool
{
    private readonly PlanningRepository _repository;

    public DeleteReminderTool(PlanningRepository repository)
    {
        _repository = repository;
    }

    public string Name => "delete_reminder";

    public OllamaToolDefinition Definition => new()
    {
        Type = "function",
        Function = new OllamaFunctionDefinition
        {
            Name = Name,
            Description =
                "Elimina o descarta un recordatorio activo del usuario actual. " +
                "Puede recibir un ID corto o una parte del título.",
            Parameters = new OllamaFunctionParameters
            {
                Type = "object",
                Required = ["reminderIdOrText"],
                Properties = new()
                {
                    ["reminderIdOrText"] = new OllamaFunctionParameterProperty
                    {
                        Type = "string",
                        Description =
                            "ID del recordatorio o texto suficiente para identificarlo."
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

        var reminderIdOrText = ToolArgumentReader.GetOptionalString(
            arguments,
            "reminderIdOrText"
        );

        if (string.IsNullOrWhiteSpace(reminderIdOrText))
        {
            return ToolExecutionResult.Fail(
                "No se ha indicado qué recordatorio debe eliminarse."
            );
        }

        var matches = await _repository.FindActiveRemindersAsync(
            executionContext.OwnerKey,
            reminderIdOrText,
            cancellationToken
        );

        if (matches.Count == 0)
        {
            return ToolExecutionResult.Fail(
                "No se ha encontrado ningún recordatorio activo que coincida."
            );
        }

        if (matches.Count > 1)
        {
            var sb = new StringBuilder();

            sb.AppendLine(
                "He encontrado varios recordatorios posibles. " +
                "Indica uno con más precisión:"
            );

            foreach (var reminder in matches)
            {
                sb.AppendLine(
                    $"- ID {reminder.ShortId}: " +
                    $"{reminder.Title} | " +
                    $"Fecha: {reminder.RemindAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}"
                );
            }

            return ToolExecutionResult.Fail(
                sb.ToString().Trim()
            );
        }

        var selectedReminder = matches[0];

        await _repository.DismissReminderAsync(
            selectedReminder.Id,
            executionContext.OwnerKey,
            cancellationToken
        );

        return ToolExecutionResult.Ok(
            $"Recordatorio eliminado correctamente: {selectedReminder.Title}."
        );
    }
}

/// <summary>
/// Herramienta que permite eliminar o descartar
/// todos los recordatorios activos del usuario actual.
///
/// Los recordatorios no se borran físicamente:
/// se marcan como descartados para que:
/// - No aparezcan en listados.
/// - No vuelvan a notificarse.
/// </summary>
public sealed class DeleteAllRemindersTool : IAssistantServerTool
{
    private readonly PlanningRepository _repository;

    public DeleteAllRemindersTool(PlanningRepository repository)
    {
        _repository = repository;
    }

    public string Name => "delete_all_reminders";

    public OllamaToolDefinition Definition => new()
    {
        Type = "function",
        Function = new OllamaFunctionDefinition
        {
            Name = Name,
            Description =
                "Elimina o descarta todos los recordatorios activos del usuario actual. " +
                "Debe usarse solo cuando el usuario pida de forma clara borrar, eliminar o cancelar todos sus recordatorios o avisos.",
            Parameters = new OllamaFunctionParameters
            {
                Type = "object",
                Required = [],
                Properties = new()
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

        var dismissedCount = await _repository.DismissAllRemindersAsync(
            executionContext.OwnerKey,
            cancellationToken
        );

        if (dismissedCount == 0)
        {
            return ToolExecutionResult.Ok(
                "No había recordatorios activos que eliminar."
            );
        }

        return ToolExecutionResult.Ok(
            $"Se han eliminado correctamente {dismissedCount} recordatorio(s) activos del usuario."
        );
    }
}