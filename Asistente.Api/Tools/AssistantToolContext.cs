/// <summary>
/// Información del equipo cliente y del usuario que está usando el asistente.
/// 
/// Esta información será enviada desde la aplicación Desktop
/// para poder asociar tareas y recordatorios a una persona concreta.
/// </summary>
public sealed class AssistantClientContext
{
    /// <summary>
    /// Nombre de usuario de Windows.
    /// Ejemplo: equipo02
    /// </summary>
    public string UserName { get; set; } = "";

    /// <summary>
    /// Dominio o grupo de trabajo.
    /// Ejemplo: SUPERMERCADO
    /// </summary>
    public string DomainName { get; set; } = "";

    /// <summary>
    /// Nombre del equipo desde el que se hace la consulta.
    /// </summary>
    public string MachineName { get; set; } = "";

    /// <summary>
    /// Nombre visible del usuario.
    /// De momento puede coincidir con UserName.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Construye una clave única para identificar al propietario
    /// de tareas y recordatorios.
    /// 
    /// Si existe dominio:
    ///   dominio\usuario
    /// 
    /// Si no:
    ///   usuario
    /// </summary>
    public string BuildOwnerKey()
    {
        if (!string.IsNullOrWhiteSpace(DomainName) &&
            !string.IsNullOrWhiteSpace(UserName))
        {
            return $"{DomainName}\\{UserName}".ToLowerInvariant();
        }

        return UserName.ToLowerInvariant();
    }
}


/// <summary>
/// Contexto de ejecución que se entrega a cada herramienta.
/// 
/// A partir de ahora, las herramientas podrán saber
/// qué usuario ha solicitado la acción.
/// </summary>
public sealed class AssistantToolExecutionContext
{
    public AssistantClientContext Client { get; }

    /// <summary>
    /// Clave identificadora única del usuario.
    /// Será la que usemos en la base de datos.
    /// </summary>
    public string OwnerKey => Client.BuildOwnerKey();

    /// <summary>
    /// Indica si se ha recibido al menos un nombre de usuario válido.
    /// </summary>
    public bool HasIdentifiedUser =>
        !string.IsNullOrWhiteSpace(Client.UserName);

    public AssistantToolExecutionContext(AssistantClientContext client)
    {
        Client = client ?? new AssistantClientContext();
    }
}