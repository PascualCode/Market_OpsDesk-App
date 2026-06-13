/// <summary>
/// Configuración del directorio telefónico interno.
///
/// Permite definir:
/// - Si el módulo está habilitado.
/// - La ruta del JSON con los contactos.
/// </summary>
public sealed class PhoneDirectoryOptions
{
    public bool Enabled { get; set; } = true;

    public string FilePath { get; set; } =
        "/opt/asistente/directory/phone-directory.json";
}