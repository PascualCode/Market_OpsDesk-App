using Asistente.Api.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Asistente.Api.Posters;

/// <summary>
/// Repositorio de solo lectura para obtener datos de producto
/// desde SQL Server.
///
/// Importante:
/// - Solo usa consultas parametrizadas.
/// - No ejecuta SQL dinámico recibido del usuario.
/// - El usuario SQL debe ser de solo lectura.
/// </summary>
public sealed class PosterProductRepository
{
    private readonly PosterDatabaseOptions _options;

    public PosterProductRepository(
        IOptions<PosterDatabaseOptions> options)
    {
        _options = options.Value;
    }

    public async Task<PosterProductLookupResult> FindByCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return new PosterProductLookupResult
            {
                Found = false,
                Message = "No se ha indicado ningún código de producto."
            };
        }

        await using var connection = new SqlConnection(
            _options.ConnectionString
        );

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandTimeout = _options.CommandTimeoutSeconds;

        /*
         * CONSULTA TEMPORAL.
         *
         * Aquí tendremos que poner la consulta real cuando sepamos:
         * - tabla o vista de productos,
         * - campo del código,
         * - campo del nombre,
         * - campo del precio,
         * - etc.
         */
        command.CommandText = """
            SELECT TOP (1)
                Codigo = v.[Código Articulo],
                Nombre = v.Descripción,
                Precio = vm.PrecioTarifa,
                IvaCode = vm.Iva,
                Ean = v.[Código Barras]
            FROM Vista_SGA_Select_CodigoBarras v
            INNER JOIN Vista_Etiquetas_PreciosModi vm
                ON v.[Código Articulo] = vm.[Código Artículo]
            WHERE v.[Código Barras] = @code
              AND vm.Almacen = @warehouse
              AND vm.[Código Tarifa] = @rateCode
            ORDER BY v.[Código Articulo];
            """;

        command.Parameters.AddWithValue("@code", code.Trim());
        command.Parameters.AddWithValue("@warehouse", "01");
        command.Parameters.AddWithValue("@rateCode", "CH");

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return PosterProductLookupResult.NotFound(code);
        }

        var product = MapProduct(reader);

        if (reader["Precio"] is not DBNull)
        {
            product.Price = Convert.ToDecimal(reader["Precio"]);
        }

        return PosterProductLookupResult.Ok(product);
    }

    /// <summary>
    /// Busca productos en SQL Server para el generador de carteles.
    ///
    /// Este método permite buscar productos de tres maneras:
    /// - Por EAN / código de barras.
    /// - Por código interno de artículo.
    /// - Por fragmento de nombre o descripción.
    ///
    /// La búsqueda se realiza siempre mediante parámetros SQL,
    /// evitando concatenar directamente texto introducido por el usuario.
    /// Esto reduce riesgos y mantiene la consulta controlada.
    /// </summary>
    public async Task<List<PosterProductDto>> SearchAsync(
    string query,
    PosterProductSearchType searchType,
    int maxResults,
    CancellationToken cancellationToken)
    {
        var products = new List<PosterProductDto>();

        if (string.IsNullOrWhiteSpace(query))
            return products;

        //RESULTADOS OBSERVABLES EN LA BUSQUEDA
        maxResults = Math.Clamp(
            maxResults,
            1,
            200
        );

        await using var connection = new SqlConnection(
            _options.ConnectionString
        );

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandTimeout =
            _options.CommandTimeoutSeconds;

        var whereClause = searchType switch
        {
            PosterProductSearchType.ArticleCode =>
                "v.[Código Articulo] = @query",

            PosterProductSearchType.Name =>
                "v.Descripción LIKE @queryLike AND v.Descripción NOT LIKE 'XXX%'",

            _ =>
                "v.[Código Barras] = @query"
        };

        command.CommandText = $"""
            SELECT TOP (@maxResults)
                Codigo = v.[Código Articulo],
                Nombre = MAX(v.Descripción),
                Precio = MAX(vm.PrecioTarifa),
                IvaCode = MAX(vm.Iva),
                Ean = MIN(v.[Código Barras])
            FROM Vista_SGA_Select_CodigoBarras v
            INNER JOIN Vista_Etiquetas_PreciosModi vm
                ON v.[Código Articulo] = vm.[Código Artículo]
            WHERE {whereClause}
              AND vm.Almacen = @warehouse
              AND vm.[Código Tarifa] = @rateCode
            GROUP BY v.[Código Articulo]
            ORDER BY MAX(v.Descripción);
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
            "01"
        );

        command.Parameters.AddWithValue(
            "@rateCode",
            "CH"
        );

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(MapProduct(reader));
        }

        return products;
    }

    /// <summary>
    /// Recupera varios productos a partir de sus códigos internos de artículo.
    ///
    /// Se usa para la generación multiartículo de carteles.
    /// La lista recibida viene desde el Desktop con los productos seleccionados.
    ///
    /// Seguridad:
    /// - No se concatena texto del usuario directamente en valores SQL.
    /// - Se generan parámetros individuales: @code0, @code1, @code2...
    /// - La consulta sigue siendo solo lectura.
    /// </summary>
    public async Task<List<PosterProductDto>> FindByArticleCodesAsync(
        IReadOnlyList<string> articleCodes,
        CancellationToken cancellationToken)
    {
        var cleanedCodes = articleCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Take(200)
            .ToList();

        var uniqueCodes = cleanedCodes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleanedCodes.Count == 0)
            return [];

        await using var connection = new SqlConnection(
            _options.ConnectionString
        );

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandTimeout =
            _options.CommandTimeoutSeconds;

        var parameterNames = new List<string>();

        for (var i = 0; i < uniqueCodes.Count; i++)
        {
            var parameterName =
                $"@code{i}";

            parameterNames.Add(parameterName);

            command.Parameters.AddWithValue(
                parameterName,
                uniqueCodes[i]
            );
        }

        var inClause =
            string.Join(
                ", ",
                parameterNames
            );

        command.CommandText = $"""
        SELECT
            Codigo = v.[Código Articulo],
            Nombre = MAX(v.Descripción),
            Precio = MAX(vm.PrecioTarifa),
            IvaCode = MAX(vm.Iva),
            Ean = MIN(v.[Código Barras])
        FROM Vista_SGA_Select_CodigoBarras v
        INNER JOIN Vista_Etiquetas_PreciosModi vm
            ON v.[Código Articulo] = vm.[Código Artículo]
        WHERE v.[Código Articulo] IN ({inClause})
          AND vm.Almacen = @warehouse
          AND vm.[Código Tarifa] = @rateCode
        GROUP BY v.[Código Articulo];
        """;

        command.Parameters.AddWithValue(
            "@warehouse",
            "01"
        );

        command.Parameters.AddWithValue(
            "@rateCode",
            "CH"
        );

        var productsByCode =
            new Dictionary<string, PosterProductDto>(
                StringComparer.OrdinalIgnoreCase
            );

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var product =
                MapProduct(reader);

            if (!string.IsNullOrWhiteSpace(product.Code))
            {
                productsByCode[product.Code] = product;
            }
        }

        /*
         * Devolvemos los productos en el mismo orden en el que
         * fueron seleccionados en el Desktop.
         */
        var orderedProducts = new List<PosterProductDto>();

        foreach (var code in cleanedCodes)
        {
            if (productsByCode.TryGetValue(code, out var product))
            {
                orderedProducts.Add(product);
            }
        }

        return orderedProducts;
    }

    /// <summary>
    /// Convierte una fila leída desde SQL Server en un objeto PosterProductDto.
    ///
    /// Este método centraliza el mapeo de columnas para evitar repetir
    /// el mismo código en diferentes consultas:
    /// - búsqueda por EAN,
    /// - búsqueda por código artículo,
    /// - búsqueda por nombre,
    /// - futura búsqueda multiartículo.
    ///
    /// La consulta SQL debe devolver los alias:
    /// - Codigo
    /// - Nombre
    /// - Precio
    /// - IvaCode
    /// - Ean
    /// </summary>
    private static PosterProductDto MapProduct(
    SqlDataReader reader)
    {
        var product = new PosterProductDto
        {
            Code = reader["Codigo"]?.ToString() ?? "",
            Name = reader["Nombre"]?.ToString() ?? "",
            Ean = reader["Ean"]?.ToString(),
            VatCode = reader["IvaCode"]?.ToString(),
            Brand = null,
            Format = null,
            Unit = null
        };

        if (reader["Precio"] is not DBNull)
        {
            product.Price = Convert.ToDecimal(
                reader["Precio"]
            );
        }

        return product;
    }
}