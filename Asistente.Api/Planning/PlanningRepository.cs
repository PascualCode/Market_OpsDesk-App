using Microsoft.Data.Sqlite;

/// <summary>
/// Repositorio encargado de guardar y consultar tareas y recordatorios
/// dentro de la base de datos SQLite planning.db.
/// </summary>
public sealed class PlanningRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// Constructor del repositorio.
    ///
    /// Funciones:
    /// - Lee la ruta de la base de datos desde appsettings.json.
    /// - Crea la carpeta de almacenamiento si no existe.
    /// - Construye la cadena de conexión SQLite.
    /// - Garantiza que las tablas estén creadas.
    /// </summary>
    public PlanningRepository(IConfiguration configuration)
    {
        var options = configuration
            .GetSection("PlanningStorage")
            .Get<PlanningStorageOptions>() ?? new PlanningStorageOptions();

        var directory = Path.GetDirectoryName(options.DatabasePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath
        }.ToString();

        EnsureDatabase();
    }

    /// <summary>
    /// Crea las tablas e índices necesarios si todavía no existen.
    ///
    /// Tablas:
    /// - tasks
    /// - reminders
    /// </summary>
    private void EnsureDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = """
        CREATE TABLE IF NOT EXISTS tasks (
            id TEXT PRIMARY KEY,
            owner_key TEXT NOT NULL,
            title TEXT NOT NULL,
            notes TEXT NULL,
            due_at_utc TEXT NULL,
            due_date_local TEXT NULL,
            status TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            completed_at_utc TEXT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_tasks_owner_status
        ON tasks(owner_key, status);

        CREATE TABLE IF NOT EXISTS reminders (
            id TEXT PRIMARY KEY,
            owner_key TEXT NOT NULL,
            title TEXT NOT NULL,
            notes TEXT NULL,
            remind_at_utc TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            notified_at_utc TEXT NULL,
            dismissed_at_utc TEXT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_reminders_owner_time
        ON reminders(owner_key, remind_at_utc);
        """;

        command.ExecuteNonQuery();
        EnsureDueDateLocalColumnExists(connection);
    }

    /// <summary>
    /// Garantiza que la columna due_date_local exista en la tabla tasks.
    ///
    /// Esta migración es necesaria para bases de datos ya creadas
    /// antes de incorporar tareas con fecha sin hora.
    /// </summary>
    private static void EnsureDueDateLocalColumnExists(
        SqliteConnection connection)
    {
        using var checkCommand = connection.CreateCommand();

        checkCommand.CommandText = """
                                PRAGMA table_info(tasks);
                                """;

        using var reader = checkCommand.ExecuteReader();

        var columnExists = false;

        while (reader.Read())
        {
            var columnName = reader.GetString(1);

            if (string.Equals(
                    columnName,
                    "due_date_local",
                    StringComparison.OrdinalIgnoreCase))
            {
                columnExists = true;
                break;
            }
        }

        reader.Close();

        if (columnExists)
            return;

        using var alterCommand = connection.CreateCommand();

        alterCommand.CommandText = """
                                ALTER TABLE tasks
                                ADD COLUMN due_date_local TEXT NULL;
                                """;

        alterCommand.ExecuteNonQuery();
    }

    /// <summary>
    /// Crea una nueva tarea para un usuario.
    /// </summary>
    public async Task<AssistantTaskItem> CreateTaskAsync(
        string ownerKey,
        string title,
        string? notes,
        DateTimeOffset? dueAtUtc,
        string? dueDateLocal,
        CancellationToken cancellationToken)
    {
        var task = new AssistantTaskItem
        {
            Id = Guid.NewGuid().ToString(),
            OwnerKey = ownerKey,
            Title = title,
            Notes = notes,
            DueAtUtc = dueAtUtc,
            DueDateLocal = dueDateLocal,
            Status = "pending",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = """
        INSERT INTO tasks (
            id,
            owner_key,
            title,
            notes,
            due_at_utc,
            due_date_local,
            status,
            created_at_utc,
            completed_at_utc
        )
        VALUES (
            @id,
            @owner_key,
            @title,
            @notes,
            @due_at_utc,
            @due_date_local,
            @status,
            @created_at_utc,
            NULL
        );
        """;

        command.Parameters.AddWithValue("@id", task.Id);
        command.Parameters.AddWithValue("@owner_key", task.OwnerKey);
        command.Parameters.AddWithValue("@title", task.Title);
        command.Parameters.AddWithValue(
            "@notes",
            (object?)task.Notes ?? DBNull.Value
        );
        command.Parameters.AddWithValue(
            "@due_at_utc",
            task.DueAtUtc?.ToString("O") ?? (object)DBNull.Value
        );
        command.Parameters.AddWithValue(
            "@due_date_local",
            task.DueDateLocal ?? (object)DBNull.Value
        );
        command.Parameters.AddWithValue("@status", task.Status);
        command.Parameters.AddWithValue(
            "@created_at_utc",
            task.CreatedAtUtc.ToString("O")
        );

        await command.ExecuteNonQueryAsync(cancellationToken);

        return task;
    }

    /// <summary>
    /// Devuelve tareas de un usuario según el ámbito solicitado.
    ///
    /// Scopes disponibles:
    /// - pending   → tareas pendientes
    /// - today     → tareas pendientes previstas para hoy
    /// - completed → tareas finalizadas
    /// - all       → todas
    /// </summary>
    public async Task<List<AssistantTaskItem>> ListTasksAsync(
        string ownerKey,
        string scope,
        CancellationToken cancellationToken)
    {
        var normalizedScope = scope.Trim().ToLowerInvariant();

        var whereClause = normalizedScope switch
        {
            "completed" =>
                "owner_key = @owner_key AND status = 'completed'",

            "all" =>
                "owner_key = @owner_key",

            "today" =>
                "owner_key = @owner_key AND status = 'pending' " +
                "AND (" +
                    "due_date_local = @today_local " +
                    "OR (due_at_utc >= @start_utc AND due_at_utc < @end_utc)" +
                ")",

            _ =>
                "owner_key = @owner_key AND status = 'pending'"
        };

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = $"""
        SELECT
            id,
            owner_key,
            title,
            notes,
            due_at_utc,
            due_date_local,
            status,
            created_at_utc,
            completed_at_utc
        FROM tasks
        WHERE {whereClause}
        ORDER BY
            CASE
                WHEN due_at_utc IS NULL AND due_date_local IS NULL THEN 1
                ELSE 0
            END,
            COALESCE(due_date_local, substr(due_at_utc, 1, 10)) ASC,
            due_at_utc ASC,
            created_at_utc DESC;
        """;

        command.Parameters.AddWithValue("@owner_key", ownerKey);

        if (normalizedScope == "today")
        {
            var now = DateTimeOffset.Now;
            var startLocal = new DateTimeOffset(now.Date, now.Offset);
            var endLocal = startLocal.AddDays(1);

            command.Parameters.AddWithValue(
                "@today_local",
                startLocal.ToString("yyyy-MM-dd")
            );

            command.Parameters.AddWithValue(
                "@start_utc",
                startLocal.ToUniversalTime().ToString("O")
            );

            command.Parameters.AddWithValue(
                "@end_utc",
                endLocal.ToUniversalTime().ToString("O")
            );
        }

        var items = new List<AssistantTaskItem>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadTask(reader));
        }

        return items;
    }

    /// <summary>
    /// Busca tareas pendientes por:
    /// - ID completo o ID corto.
    /// - Texto contenido en el título.
    ///
    /// Se utilizará cuando el usuario pida:
    /// "Marca como completada la tarea de revisar facturas".
    /// </summary>
    public async Task<List<AssistantTaskItem>> FindPendingTasksAsync(
        string ownerKey,
        string taskIdOrText,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = """
        SELECT
            id,
            owner_key,
            title,
            notes,
            due_at_utc,
            due_date_local,
            status,
            created_at_utc,
            completed_at_utc
        FROM tasks
        WHERE owner_key = @owner_key
          AND status = 'pending'
          AND (
              id LIKE @id_prefix
              OR lower(title) LIKE '%' || lower(@text_search) || '%'
          )
        ORDER BY created_at_utc DESC
        LIMIT 10;
        """;

        command.Parameters.AddWithValue(
            "@owner_key",
            ownerKey
        );

        command.Parameters.AddWithValue(
            "@id_prefix",
            taskIdOrText + "%"
        );

        command.Parameters.AddWithValue(
            "@text_search",
            taskIdOrText
        );

        var items = new List<AssistantTaskItem>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadTask(reader));
        }

        return items;
    }

    /// <summary>
    /// Marca una tarea pendiente como completada.
    /// </summary>
    public async Task MarkTaskCompletedAsync(
        string taskId,
        string ownerKey,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = """
        UPDATE tasks
        SET status = 'completed',
            completed_at_utc = @completed_at_utc
        WHERE id = @id
          AND owner_key = @owner_key
          AND status = 'pending';
        """;

        command.Parameters.AddWithValue(
            "@id",
            taskId
        );

        command.Parameters.AddWithValue(
            "@owner_key",
            ownerKey
        );

        command.Parameters.AddWithValue(
            "@completed_at_utc",
            DateTimeOffset.UtcNow.ToString("O")
        );

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Elimina definitivamente una tarea del usuario.
    /// 
    /// Solo se elimina si pertenece al ownerKey indicado.
    /// </summary>
    public async Task DeleteTaskAsync(
        string taskId,
        string ownerKey,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
    DELETE FROM tasks
    WHERE id = @id
      AND owner_key = @owner_key;
    """;

        command.Parameters.AddWithValue(
            "@id",
            taskId
        );

        command.Parameters.AddWithValue(
            "@owner_key",
            ownerKey
        );

        await command.ExecuteNonQueryAsync(
            cancellationToken
        );
    }

    /// <summary>
    /// Elimina definitivamente todas las tareas
    /// pertenecientes al usuario indicado.
    ///
    /// Incluye:
    /// - Tareas pendientes.
    /// - Tareas completadas.
    /// </summary>
    public async Task<int> DeleteAllTasksAsync(
        string ownerKey,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
    DELETE FROM tasks
    WHERE owner_key = @owner_key;
    """;

        command.Parameters.AddWithValue(
            "@owner_key",
            ownerKey
        );

        var affectedRows =
            await command.ExecuteNonQueryAsync(cancellationToken);

        return affectedRows;
    }

    /// <summary>
    /// Busca recordatorios activos del usuario por:
    /// - ID completo o ID corto.
    /// - Texto contenido en el título.
    ///
    /// Se consideran activos todos los recordatorios
    /// que todavía no han sido descartados.
    /// </summary>
    public async Task<List<AssistantReminderItem>> FindActiveRemindersAsync(
        string ownerKey,
        string reminderIdOrText,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
                            SELECT
                                id,
                                owner_key,
                                title,
                                notes,
                                remind_at_utc,
                                created_at_utc,
                                notified_at_utc,
                                dismissed_at_utc
                            FROM reminders
                            WHERE owner_key = @owner_key
                              AND dismissed_at_utc IS NULL
                              AND (
                                  id LIKE @id_prefix
                                  OR lower(title) LIKE '%' || lower(@text_search) || '%'
                              )
                            ORDER BY remind_at_utc DESC
                            LIMIT 10;
                            """;

        command.Parameters.AddWithValue(
            "@owner_key",
            ownerKey
        );

        command.Parameters.AddWithValue(
            "@id_prefix",
            reminderIdOrText + "%"
        );

        command.Parameters.AddWithValue(
            "@text_search",
            reminderIdOrText
        );

        var items = new List<AssistantReminderItem>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadReminder(reader));
        }

        return items;
    }

    /// <summary>
    /// Descarta lógicamente un recordatorio.
    ///
    /// No se elimina físicamente de la base de datos:
    /// se marca dismissed_at_utc para que:
    /// - No aparezca en listados activos.
    /// - No vuelva a notificarse.
    /// </summary>
    public async Task DismissReminderAsync(
        string reminderId,
        string ownerKey,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
                UPDATE reminders
                SET dismissed_at_utc = @dismissed_at_utc
                WHERE id = @id
                  AND owner_key = @owner_key
                  AND dismissed_at_utc IS NULL;
                """;

        command.Parameters.AddWithValue(
            "@id",
            reminderId
        );

        command.Parameters.AddWithValue(
            "@owner_key",
            ownerKey
        );

        command.Parameters.AddWithValue(
            "@dismissed_at_utc",
            DateTimeOffset.UtcNow.ToString("O")
        );

        await command.ExecuteNonQueryAsync(
            cancellationToken
        );
    }

    /// <summary>
    /// Descarta todos los recordatorios activos
    /// pertenecientes al usuario indicado.
    ///
    /// No los elimina físicamente:
    /// se marca dismissed_at_utc para que:
    /// - No aparezcan en listados activos.
    /// - No vuelvan a notificarse.
    /// </summary>
    public async Task<int> DismissAllRemindersAsync(
        string ownerKey,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
    UPDATE reminders
    SET dismissed_at_utc = @dismissed_at_utc
    WHERE owner_key = @owner_key
      AND dismissed_at_utc IS NULL;
    """;

        command.Parameters.AddWithValue(
            "@owner_key",
            ownerKey
        );

        command.Parameters.AddWithValue(
            "@dismissed_at_utc",
            DateTimeOffset.UtcNow.ToString("O")
        );

        var affectedRows =
            await command.ExecuteNonQueryAsync(cancellationToken);

        return affectedRows;
    }

    /// <summary>
    /// Crea un nuevo recordatorio para un usuario.
    /// </summary>
    public async Task<AssistantReminderItem> CreateReminderAsync(
        string ownerKey,
        string title,
        string? notes,
        DateTimeOffset remindAtUtc,
        CancellationToken cancellationToken)
    {
        var reminder = new AssistantReminderItem
        {
            Id = Guid.NewGuid().ToString(),
            OwnerKey = ownerKey,
            Title = title,
            Notes = notes,
            RemindAtUtc = remindAtUtc,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = """
        INSERT INTO reminders (
            id,
            owner_key,
            title,
            notes,
            remind_at_utc,
            created_at_utc,
            notified_at_utc,
            dismissed_at_utc
        )
        VALUES (
            @id,
            @owner_key,
            @title,
            @notes,
            @remind_at_utc,
            @created_at_utc,
            NULL,
            NULL
        );
        """;

        command.Parameters.AddWithValue(
            "@id",
            reminder.Id
        );

        command.Parameters.AddWithValue(
            "@owner_key",
            reminder.OwnerKey
        );

        command.Parameters.AddWithValue(
            "@title",
            reminder.Title
        );

        command.Parameters.AddWithValue(
            "@notes",
            (object?)reminder.Notes ?? DBNull.Value
        );

        command.Parameters.AddWithValue(
            "@remind_at_utc",
            reminder.RemindAtUtc.ToString("O")
        );

        command.Parameters.AddWithValue(
            "@created_at_utc",
            reminder.CreatedAtUtc.ToString("O")
        );

        await command.ExecuteNonQueryAsync(cancellationToken);

        return reminder;
    }

    /// <summary>
    /// Lista recordatorios de un usuario según el ámbito solicitado.
    ///
    /// Scopes disponibles:
    /// - upcoming → próximos recordatorios futuros
    /// - today    → recordatorios previstos para hoy
    /// - all      → todos los recordatorios no descartados
    /// </summary>
    public async Task<List<AssistantReminderItem>> ListRemindersAsync(
        string ownerKey,
        string scope,
        CancellationToken cancellationToken)
    {
        var normalizedScope = scope.Trim().ToLowerInvariant();

        var nowUtc = DateTimeOffset.UtcNow.ToString("O");

        var whereClause = normalizedScope switch
        {
            "all" =>
                "owner_key = @owner_key AND dismissed_at_utc IS NULL",

            "today" =>
                "owner_key = @owner_key AND dismissed_at_utc IS NULL " +
                "AND remind_at_utc >= @start_utc AND remind_at_utc < @end_utc",

            _ =>
                "owner_key = @owner_key AND dismissed_at_utc IS NULL " +
                "AND remind_at_utc >= @now_utc"
        };

        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = $"""
        SELECT
            id,
            owner_key,
            title,
            notes,
            remind_at_utc,
            created_at_utc,
            notified_at_utc,
            dismissed_at_utc
        FROM reminders
        WHERE {whereClause}
        ORDER BY remind_at_utc ASC;
        """;

        command.Parameters.AddWithValue(
            "@owner_key",
            ownerKey
        );

        command.Parameters.AddWithValue(
            "@now_utc",
            nowUtc
        );

        if (normalizedScope == "today")
        {
            var now = DateTimeOffset.Now;
            var startLocal = new DateTimeOffset(now.Date, now.Offset);
            var endLocal = startLocal.AddDays(1);

            command.Parameters.AddWithValue(
                "@start_utc",
                startLocal.ToUniversalTime().ToString("O")
            );

            command.Parameters.AddWithValue(
                "@end_utc",
                endLocal.ToUniversalTime().ToString("O")
            );
        }

        var items = new List<AssistantReminderItem>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadReminder(reader));
        }

        return items;
    }

    /// <summary>
    /// Devuelve recordatorios cuyo momento de aviso ya ha llegado
    /// y que todavía no han sido notificados al usuario.
    /// </summary>
    public async Task<List<AssistantReminderItem>> GetDueUnnotifiedRemindersAsync(
        string ownerKey,
        int maxResults,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = """
        SELECT
            id,
            owner_key,
            title,
            notes,
            remind_at_utc,
            created_at_utc,
            notified_at_utc,
            dismissed_at_utc
        FROM reminders
        WHERE owner_key = @owner_key
          AND dismissed_at_utc IS NULL
          AND notified_at_utc IS NULL
          AND remind_at_utc <= @now_utc
        ORDER BY remind_at_utc ASC
        LIMIT @max_results;
        """;

        command.Parameters.AddWithValue(
            "@owner_key",
            ownerKey
        );

        command.Parameters.AddWithValue(
            "@now_utc",
            DateTimeOffset.UtcNow.ToString("O")
        );

        command.Parameters.AddWithValue(
            "@max_results",
            maxResults
        );

        var items = new List<AssistantReminderItem>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadReminder(reader));
        }

        return items;
    }

    /// <summary>
    /// Marca un recordatorio como ya notificado al usuario.
    /// </summary>
    public async Task MarkReminderAsNotifiedAsync(
        string reminderId,
        string ownerKey,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = """
        UPDATE reminders
        SET notified_at_utc = @notified_at_utc
        WHERE id = @id
          AND owner_key = @owner_key
          AND notified_at_utc IS NULL;
        """;

        command.Parameters.AddWithValue(
            "@id",
            reminderId
        );

        command.Parameters.AddWithValue(
            "@owner_key",
            ownerKey
        );

        command.Parameters.AddWithValue(
            "@notified_at_utc",
            DateTimeOffset.UtcNow.ToString("O")
        );

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Convierte una fila de SQLite en un objeto AssistantTaskItem.
    /// </summary>
    private static AssistantTaskItem ReadTask(SqliteDataReader reader)
    {
        return new AssistantTaskItem
        {
            Id = reader.GetString(0),
            OwnerKey = reader.GetString(1),
            Title = reader.GetString(2),
            Notes = reader.IsDBNull(3)
                ? null
                : reader.GetString(3),

            DueAtUtc = reader.IsDBNull(4)
                ? null
                : DateTimeOffset.Parse(reader.GetString(4)),

            DueDateLocal = reader.IsDBNull(5)
                ? null
                : reader.GetString(5),

            Status = reader.GetString(6),

            CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(7)),

            CompletedAtUtc = reader.IsDBNull(8)
                ? null
                : DateTimeOffset.Parse(reader.GetString(8))
        };
    }

    /// <summary>
    /// Convierte una fila de SQLite en un objeto AssistantReminderItem.
    /// </summary>
    private static AssistantReminderItem ReadReminder(SqliteDataReader reader)
    {
        return new AssistantReminderItem
        {
            Id = reader.GetString(0),
            OwnerKey = reader.GetString(1),
            Title = reader.GetString(2),
            Notes = reader.IsDBNull(3)
                ? null
                : reader.GetString(3),
            RemindAtUtc = DateTimeOffset.Parse(reader.GetString(4)),
            CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(5)),
            NotifiedAtUtc = reader.IsDBNull(6)
                ? null
                : DateTimeOffset.Parse(reader.GetString(6)),
            DismissedAtUtc = reader.IsDBNull(7)
                ? null
                : DateTimeOffset.Parse(reader.GetString(7))
        };
    }
}