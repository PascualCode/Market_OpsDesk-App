using Asistente.Desktop.Models;

namespace Asistente.Desktop.Services;

/// <summary>
/// Construye el contexto de identidad del usuario y equipo
/// desde el que se ejecuta el Desktop.
///
/// Este contexto se envía a la API para:
/// - Asociar conversaciones al usuario.
/// - Consultar recordatorios propios.
/// - Consultar acciones locales del equipo.
/// - Informar del resultado de acciones procesadas.
/// </summary>
public sealed class DesktopClientContextService
{
    /// <summary>
    /// Genera el contexto actual del usuario Windows y del equipo.
    /// </summary>
    public DesktopClientContext Build()
    {
        return new DesktopClientContext
        {
            UserName = Environment.UserName,
            DomainName = Environment.UserDomainName,
            MachineName = Environment.MachineName,
            DisplayName = Environment.UserName
        };
    }
}
