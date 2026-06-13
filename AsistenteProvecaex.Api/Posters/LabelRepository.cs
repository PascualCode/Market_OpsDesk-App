using Asistente.Api.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Asistente.Api.Posters;

//update PARTI_EtiLineal set Impreso = '0' where ROW_ID = 306581

/// <summary>
/// Repositorio de solo lectura para etiquetas pendientes de impresión.
///
/// De momento este repositorio NO actualiza el estado Impreso.
/// La fase de marcado como impreso queda pendiente hasta confirmar
/// la tabla real o consulta correcta de UPDATE.
/// </summary>
public sealed class LabelRepository
{
    private readonly PosterDatabaseOptions _options;

    public LabelRepository(
        IOptions<PosterDatabaseOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Obtiene las etiquetas pendientes de impresión del año actual.
    ///
    /// Criterios:
    /// - FechaActuali dentro del año actual.
    /// - Impreso = 0.
    /// - Almacen = almacén indicado, por defecto 01.
    ///
    /// Esta será la carga principal que verá el usuario
    /// al entrar en el modo Etiquetas.
    /// </summary>
    public async Task<List<PendingLabelDto>> GetPendingLabelsForCurrentYearAsync(
        string warehouse,
        int maxResults,
        CancellationToken cancellationToken)
    {
        maxResults = Math.Clamp(
            maxResults,
            1,
            1000
        );

        if (string.IsNullOrWhiteSpace(warehouse))
            warehouse = "01";

        var labels = new List<PendingLabelDto>();

        await using var connection = new SqlConnection(
            _options.ConnectionString
        );

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandTimeout =
            _options.CommandTimeoutSeconds;

        /*
         PRUEBA
        WHERE FechaActuali >= DATEFROMPARTS(YEAR(GETDATE()), 1, 1)
        AND FechaActuali <  DATEFROMPARTS(YEAR(GETDATE()) + 1, 1, 1)
         */

        command.CommandText = """
            SELECT TOP (@maxResults)
                GroupId = GRP_ID,
                ReturnCodigoUbicacion = Return_Código_Ubicación,
                Impreso = Impreso,
                Almacen = Almacen,
                Ubicacion = Ubicación,
                CodigoArticulo = [Código Artículo],
                DescripcionArticulo = [Desc. Artículo],
                CodigoTarifa = [Código Tarifa],
                PrecioOld = Precio_Old,
                PrecioNew = Precio_New,
                Precio = PrecioTarifa,
                FechaActualizacion = FechaActuali,
                CodBarra = CodBarra,
                CodBarraAux = Return_CodBarraAux,
                CodigoProveedor = [Cod. Proveedor],
                NombreProveedor = [Nombre Proveedor],
                Clave = Clave,
                IvaCode = Iva,
                UMManip = UM_Manip,
                Factor = Factor,
                DesUnidadMedida = DesUniMed,
                FechaImpresion = FechaImpre,
                Usuario = Usuario,
                ReturnRowId = Return_Row_Id
            FROM Vista_Etiquetas_PreciosModi
            WHERE FechaActuali >= DATEFROMPARTS(YEAR(GETDATE()), 1, 1)
            AND FechaActuali <  DATEFROMPARTS(YEAR(GETDATE()) + 1, 1, 1)
              AND Impreso = 0
              AND Almacen = @warehouse
            ORDER BY FechaActuali DESC, [Desc. Artículo];
            """;

        command.Parameters.AddWithValue(
            "@maxResults",
            maxResults
        );

        command.Parameters.AddWithValue(
            "@warehouse",
            warehouse.Trim()
        );

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            labels.Add(
                MapPendingLabel(reader)
            );
        }

        return labels;
    }

    /// <summary>
    /// Recupera etiquetas concretas por Return_Row_Id.
    ///
    /// Se usará para generar el PDF solo con las etiquetas
    /// que el usuario haya añadido al lote.
    /// </summary>
    public async Task<List<PendingLabelDto>> GetLabelsByRowIdsAsync(
        IReadOnlyList<long> rowIds,
        CancellationToken cancellationToken)
    {
        var cleanedRowIds = rowIds
            .Where(id => id > 0)
            .Distinct()
            .Take(1000)
            .ToList();

        if (cleanedRowIds.Count == 0)
            return [];

        await using var connection = new SqlConnection(
            _options.ConnectionString
        );

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandTimeout =
            _options.CommandTimeoutSeconds;

        var parameterNames = new List<string>();

        for (var i = 0; i < cleanedRowIds.Count; i++)
        {
            var parameterName = $"@rowId{i}";

            parameterNames.Add(parameterName);

            command.Parameters.AddWithValue(
                parameterName,
                cleanedRowIds[i]
            );
        }

        var inClause =
            string.Join(", ", parameterNames);

        command.CommandText = $"""
            SELECT
                GroupId = GRP_ID,
                ReturnCodigoUbicacion = Return_Código_Ubicación,
                Impreso = Impreso,
                Almacen = Almacen,
                Ubicacion = Ubicación,
                CodigoArticulo = [Código Artículo],
                DescripcionArticulo = [Desc. Artículo],
                CodigoTarifa = [Código Tarifa],
                PrecioOld = Precio_Old,
                PrecioNew = Precio_New,
                Precio = PrecioTarifa,
                FechaActualizacion = FechaActuali,
                CodBarra = CodBarra,
                CodBarraAux = Return_CodBarraAux,
                CodigoProveedor = [Cod. Proveedor],
                NombreProveedor = [Nombre Proveedor],
                Clave = Clave,
                IvaCode = Iva,
                UMManip = UM_Manip,
                Factor = Factor,
                DesUnidadMedida = DesUniMed,
                FechaImpresion = FechaImpre,
                Usuario = Usuario,
                ReturnRowId = Return_Row_Id
            FROM Vista_Etiquetas_PreciosModi
            WHERE Return_Row_Id IN ({inClause});
            """;

        var labelsById =
            new Dictionary<long, PendingLabelDto>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var label =
                MapPendingLabel(reader);

            labelsById[label.ReturnRowId] = label;
        }

        /*
         * Devolvemos las etiquetas en el mismo orden elegido
         * por el usuario en Desktop.
         */
        var orderedLabels =
            new List<PendingLabelDto>();

        foreach (var rowId in cleanedRowIds)
        {
            if (labelsById.TryGetValue(rowId, out var label))
            {
                orderedLabels.Add(label);
            }
        }

        return orderedLabels;
    }

    /// <summary>
    /// Convierte una fila SQL en un PendingLabelDto.
    /// </summary>
    private static PendingLabelDto MapPendingLabel(
        SqlDataReader reader)
    {
        var label = new PendingLabelDto
        {
            GroupId = reader["GroupId"]?.ToString() ?? "",
            Warehouse = reader["Almacen"]?.ToString() ?? "",
            Location = reader["Ubicacion"]?.ToString(),
            ArticleCode = reader["CodigoArticulo"]?.ToString() ?? "",
            ArticleDescription = reader["DescripcionArticulo"]?.ToString() ?? "",
            RateCode = reader["CodigoTarifa"]?.ToString() ?? "",
            Barcode = reader["CodBarra"]?.ToString(),
            BarcodeAux = reader["CodBarraAux"]?.ToString(),
            SupplierCode = reader["CodigoProveedor"]?.ToString(),
            SupplierName = reader["NombreProveedor"]?.ToString(),
            Key = reader["Clave"]?.ToString(),
            VatCode = reader["IvaCode"]?.ToString(),
            ManipulationUnit = reader["UMManip"]?.ToString(),
            UnitDescription = reader["DesUnidadMedida"]?.ToString(),
            User = reader["Usuario"]?.ToString()
        };

        if (reader["ReturnRowId"] is not DBNull)
        {
            label.ReturnRowId =
                Convert.ToInt64(reader["ReturnRowId"]);
        }

        if (reader["Impreso"] is not DBNull)
        {
            label.Printed =
                Convert.ToInt32(reader["Impreso"]);
        }

        if (reader["PrecioOld"] is not DBNull)
        {
            label.OldPrice =
                Convert.ToDecimal(reader["PrecioOld"]);
        }

        if (reader["PrecioNew"] is not DBNull)
        {
            label.NewPrice =
                Convert.ToDecimal(reader["PrecioNew"]);
        }

        if (reader["Precio"] is not DBNull)
        {
            label.Price =
                Convert.ToDecimal(reader["Precio"]);
        }

        if (reader["FechaActualizacion"] is not DBNull)
        {
            label.UpdatedAt =
                Convert.ToDateTime(reader["FechaActualizacion"]);
        }

        if (reader["Factor"] is not DBNull)
        {
            label.Factor =
                Convert.ToDecimal(reader["Factor"]);
        }

        if (reader["FechaImpresion"] is not DBNull)
        {
            label.PrintedAt =
                Convert.ToDateTime(reader["FechaImpresion"]);
        }

        return label;
    }

    /// <summary>
    /// Busca etiquetas para reposición.
    ///
    /// A diferencia de las etiquetas pendientes, esta búsqueda NO filtra
    /// por Impreso = 0 ni por año actual.
    /// 
    /// Se usa para generar etiquetas manualmente por:
    /// - EAN / código de barras.
    /// - Código artículo.
    /// - Nombre o fragmento de descripción.
    /// </summary>
    public async Task<List<PendingLabelDto>> SearchLabelsForRestockAsync(
        string query,
        PosterProductSearchType searchType,
        string warehouse,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var labels =
            new List<PendingLabelDto>();

        if (string.IsNullOrWhiteSpace(query))
            return labels;

        if (string.IsNullOrWhiteSpace(warehouse))
            warehouse = "01";

        maxResults = Math.Clamp(
            maxResults,
            1,
            200
        );

        await using var connection = new SqlConnection(
            _options.ConnectionString
        );

        await connection.OpenAsync(cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandTimeout =
            _options.CommandTimeoutSeconds;

        var whereClause = searchType switch
        {
            PosterProductSearchType.ArticleCode =>
                "[Código Artículo] = @query",

            PosterProductSearchType.Name =>
                "[Desc. Artículo] LIKE @queryLike AND [Desc. Artículo] NOT LIKE 'XXX%'",

            _ =>
                "(CodBarra = @query OR Return_CodBarraAux = @query)"
        };

        /*
         * Para reposición nos interesa una línea válida y reciente
         * por artículo, evitando duplicados históricos de la vista.
         */
        command.CommandText = $"""
        WITH EtiquetasOrdenadas AS
        (
            SELECT
                GroupId = GRP_ID,
                ReturnCodigoUbicacion = Return_Código_Ubicación,
                Impreso = Impreso,
                Almacen = Almacen,
                Ubicacion = Ubicación,
                CodigoArticulo = [Código Artículo],
                DescripcionArticulo = [Desc. Artículo],
                CodigoTarifa = [Código Tarifa],
                PrecioOld = Precio_Old,
                PrecioNew = Precio_New,
                Precio = PrecioTarifa,
                FechaActualizacion = FechaActuali,
                CodBarra = CodBarra,
                CodBarraAux = Return_CodBarraAux,
                CodigoProveedor = [Cod. Proveedor],
                NombreProveedor = [Nombre Proveedor],
                Clave = Clave,
                IvaCode = Iva,
                UMManip = UM_Manip,
                Factor = Factor,
                DesUnidadMedida = DesUniMed,
                FechaImpresion = FechaImpre,
                Usuario = Usuario,
                ReturnRowId = Return_Row_Id,
                rn = ROW_NUMBER() OVER
                (
                    PARTITION BY [Código Artículo]
                    ORDER BY FechaActuali DESC, Return_Row_Id DESC
                )
            FROM Vista_Etiquetas_PreciosModi
            WHERE Almacen = @warehouse
              AND [Código Tarifa] = @rateCode
              AND {whereClause}
        )
        SELECT TOP (@maxResults)
            GroupId,
            ReturnCodigoUbicacion,
            Impreso,
            Almacen,
            Ubicacion,
            CodigoArticulo,
            DescripcionArticulo,
            CodigoTarifa,
            PrecioOld,
            PrecioNew,
            Precio,
            FechaActualizacion,
            CodBarra,
            CodBarraAux,
            CodigoProveedor,
            NombreProveedor,
            Clave,
            IvaCode,
            UMManip,
            Factor,
            DesUnidadMedida,
            FechaImpresion,
            Usuario,
            ReturnRowId
        FROM EtiquetasOrdenadas
        WHERE rn = 1
        ORDER BY DescripcionArticulo;
        """;

        command.Parameters.AddWithValue(
            "@maxResults",
            maxResults
        );

        command.Parameters.AddWithValue(
            "@query",
            query.Trim()
        );

        command.Parameters.AddWithValue(
            "@queryLike",
            $"%{query.Trim()}%"
        );

        command.Parameters.AddWithValue(
            "@warehouse",
            warehouse.Trim()
        );

        command.Parameters.AddWithValue(
            "@rateCode",
            "CH"
        );

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            labels.Add(
                MapPendingLabel(reader)
            );
        }

        return labels;
    }

    /// <summary>
    /// Marca etiquetas como impresas en la tabla PARTI_EtiLineal.
    ///
    /// Importante:
    /// - Según el flujo actual, Impreso = 0 significa pendiente.
    /// - Por tanto, para marcar como impresa usamos Impreso = 1.
    /// - Se actualiza por ROW_ID, usando los Return_Row_Id seleccionados.
    /// </summary>
    public async Task<int> MarkLabelsAsPrintedAsync(
        IReadOnlyList<long> rowIds,
        string? user,
        CancellationToken cancellationToken)
    {
        var cleanedRowIds = rowIds
            .Where(id => id > 0)
            .Distinct()
            .Take(1000)
            .ToList();

        if (cleanedRowIds.Count == 0)
            return 0;

        await using var connection = new SqlConnection(
            _options.ConnectionString
        );

        await connection.OpenAsync(cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandTimeout =
            _options.CommandTimeoutSeconds;

        var parameterNames =
            new List<string>();

        for (var i = 0; i < cleanedRowIds.Count; i++)
        {
            var parameterName =
                $"@rowId{i}";

            parameterNames.Add(parameterName);

            command.Parameters.AddWithValue(
                parameterName,
                cleanedRowIds[i]
            );
        }

        var inClause =
            string.Join(", ", parameterNames);

        command.CommandText = $"""
        UPDATE PARTI_EtiLineal
        SET Impreso = '1'
        WHERE ROW_ID IN ({inClause});
        """;

        var updatedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken
            );

        return updatedRows;
    }
}