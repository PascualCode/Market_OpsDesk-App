using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Repositorio de acciones locales pendientes de ejecución
/// por parte del Desktop.
///
/// Utiliza la misma base SQLite configurada para planificación,
/// de forma que toda la persistencia funcional del asistente
/// queda centralizada en un único fichero.
/// </summary>
public sealed class LocalActionRepository
{
    private readonly string _connectionString;

    public LocalActionRepository(IConfiguration configuration)
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
    /// Garantiza que exista la tabla local_actions.
    /// </summary>
    private void EnsureDatabase()
    {
        using var connection =
            new SqliteConnection(_connectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText = """
        CREATE TABLE IF NOT EXISTS local_actions (
            id TEXT PRIMARY KEY,
            owner_key TEXT NOT NULL,
            machine_name TEXT NOT NULL,
            action_type TEXT NOT NULL,
            target TEXT NOT NULL,
            status TEXT NOT NULL,
            result_message TEXT NULL,
            created_at_utc TEXT NOT NULL,
            processed_at_utc TEXT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_local_actions_pending_target
        ON local_actions (
            owner_key,
            machine_name,
            status,
            created_at_utc
        );
        """;

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Crea una nueva acción local pendiente de ejecución.
    ///
    /// La acción queda asociada a:
    /// - Usuario propietario.
    /// - Equipo concreto.
    /// - Tipo de acción.
    /// - Objetivo solicitado.
    /// </summary>
    public async Task<LocalActionItem> CreateActionAsync(
        string ownerKey,
        string machineName,
        string actionType,
        string target,
        CancellationToken cancellationToken)
    {
        var action = new LocalActionItem
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerKey = ownerKey,
            MachineName = machineName,
            ActionType = actionType,
            Target = target,
            Status = "pending",
            ResultMessage = null,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProcessedAtUtc = null
        };

        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
    INSERT INTO local_actions (
        id,
        owner_key,
        machine_name,
        action_type,
        target,
        status,
        result_message,
        created_at_utc,
        processed_at_utc
    )
    VALUES (
        @id,
        @owner_key,
        @machine_name,
        @action_type,
        @target,
        @status,
        NULL,
        @created_at_utc,
        NULL
    );
    """;

        command.Parameters.AddWithValue(
            "@id",
            action.Id
        );

        command.Parameters.AddWithValue(
            "@owner_key",
            action.OwnerKey
        );

        command.Parameters.AddWithValue(
            "@machine_name",
            action.MachineName
        );

        command.Parameters.AddWithValue(
            "@action_type",
            action.ActionType
        );

        command.Parameters.AddWithValue(
            "@target",
            action.Target
        );

        command.Parameters.AddWithValue(
            "@status",
            action.Status
        );

        command.Parameters.AddWithValue(
            "@created_at_utc",
            action.CreatedAtUtc.ToString("O")
        );

        await command.ExecuteNonQueryAsync(
            cancellationToken
        );

        return action;
    }

    /// <summary>
    /// Obtiene acciones locales pendientes de ejecución
    /// para un usuario y un equipo concretos.
    ///
    /// El Desktop utilizará este método indirectamente
    /// a través de un endpoint HTTP de polling.
    /// </summary>
    public async Task<List<LocalActionItem>> GetPendingActionsAsync(
        string ownerKey,
        string machineName,
        int maxResults,
        CancellationToken cancellationToken)
    {
        maxResults = Math.Clamp(maxResults, 1, 20);

        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
    SELECT
        id,
        owner_key,
        machine_name,
        action_type,
        target,
        status,
        result_message,
        created_at_utc,
        processed_at_utc
    FROM local_actions
    WHERE owner_key = @owner_key
      AND machine_name = @machine_name
      AND status = 'pending'
    ORDER BY created_at_utc ASC
    LIMIT @max_results;
    """;

        command.Parameters.AddWithValue(
            "@owner_key",
            ownerKey
        );

        command.Parameters.AddWithValue(
            "@machine_name",
            machineName
        );

        command.Parameters.AddWithValue(
            "@max_results",
            maxResults
        );

        var actions = new List<LocalActionItem>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            actions.Add(ReadAction(reader));
        }

        return actions;
    }

    /// <summary>
    /// Marca una acción local como procesada.
    ///
    /// El Desktop llamará a este método indirectamente
    /// cuando termine de ejecutar la orden.
    ///
    /// Estado final:
    /// - completed
    /// - failed
    /// </summary>
    public async Task CompleteActionAsync(
        string actionId,
        string ownerKey,
        string machineName,
        bool success,
        string? resultMessage,
        CancellationToken cancellationToken)
    {
        await using var connection =
            new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = """
    UPDATE local_actions
    SET
        status = @status,
        result_message = @result_message,
        processed_at_utc = @processed_at_utc
    WHERE id = @id
      AND owner_key = @owner_key
      AND machine_name = @machine_name
      AND status = 'pending';
    """;

        command.Parameters.AddWithValue(
            "@id",
            actionId
        );

        command.Parameters.AddWithValue(
            "@owner_key",
            ownerKey
        );

        command.Parameters.AddWithValue(
            "@machine_name",
            machineName
        );

        command.Parameters.AddWithValue(
            "@status",
            success ? "completed" : "failed"
        );

        command.Parameters.AddWithValue(
            "@result_message",
            string.IsNullOrWhiteSpace(resultMessage)
                ? (object)DBNull.Value
                : resultMessage
        );

        command.Parameters.AddWithValue(
            "@processed_at_utc",
            DateTimeOffset.UtcNow.ToString("O")
        );

        await command.ExecuteNonQueryAsync(
            cancellationToken
        );
    }

    /// <summary>
    /// Convierte una fila de SQLite en un LocalActionItem.
    /// </summary>
    private static LocalActionItem ReadAction(
        SqliteDataReader reader)
    {
        return new LocalActionItem
        {
            Id = reader.GetString(0),
            OwnerKey = reader.GetString(1),
            MachineName = reader.GetString(2),
            ActionType = reader.GetString(3),
            Target = reader.GetString(4),
            Status = reader.GetString(5),

            ResultMessage = reader.IsDBNull(6)
                ? null
                : reader.GetString(6),

            CreatedAtUtc =
                DateTimeOffset.Parse(reader.GetString(7)),

            ProcessedAtUtc = reader.IsDBNull(8)
                ? null
                : DateTimeOffset.Parse(reader.GetString(8))
        };
    }

}