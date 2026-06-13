namespace Asistente.Api.Configuration;

/// <summary>
/// Configuración visual y de cálculo para los carteles.
/// </summary>
public sealed class PosterRenderOptions
{
    /// <summary>
    /// Ruta del logo de la empresa en el servidor Linux.
    /// </summary>
    public string LogoPath { get; set; } =
        "/opt/asistente/assets/logo.png";

    /// <summary>
    /// Mapa de códigos de IVA devueltos por la BD.
    /// Ejemplo:
    /// GEN = 4
    /// IVA21 = 21
    /// </summary>
    public Dictionary<string, decimal> VatRatesByCode { get; set; } = new()
    {
        ["GEN"] = 21m,
        ["RED"] = 10m,
        ["RET"] = 10m,
        ["SRE"] = 4m,
        ["000"] = 4m,
        ["YOG"] = 4m
    };
}