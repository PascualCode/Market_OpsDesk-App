namespace Asistente.Api.Posters;

/// <summary>
/// Datos mínimos de producto necesarios para generar
/// un cartel o etiqueta.
/// </summary>
public sealed class PosterProductDto
{
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Brand { get; set; }

    public string? Format { get; set; }

    public decimal? Price { get; set; }

    /// <summary>
    /// Código de IVA devuelto por la base de datos.
    /// Ejemplo: GEN.
    /// </summary>
    public string? VatCode { get; set; }

    public string? Unit { get; set; }

    public string? Ean { get; set; }
}

/// <summary>
/// Resultado de búsqueda de producto.
/// </summary>
public sealed class PosterProductLookupResult
{
    public bool Found { get; set; }

    public string? Message { get; set; }

    public PosterProductDto? Product { get; set; }

    public static PosterProductLookupResult NotFound(string code)
    {
        return new PosterProductLookupResult
        {
            Found = false,
            Message = $"No se ha encontrado ningún producto con el código '{code}'."
        };
    }

    public static PosterProductLookupResult Ok(PosterProductDto product)
    {
        return new PosterProductLookupResult
        {
            Found = true,
            Product = product
        };
    }
}