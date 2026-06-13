/// <summary>
/// Endpoints de administración del directorio telefónico interno.
/// </summary>
public static class PhoneDirectoryEndpoints
{
    /// <summary>
    /// Registra las rutas del módulo de directorio telefónico.
    /// </summary>
    public static WebApplication MapPhoneDirectoryEndpoints(
        this WebApplication app)
    {
        // -------------------------------------------------------------
        // POST /api/phone-directory/reload
        // -------------------------------------------------------------
        // Recarga en memoria el fichero JSON del directorio telefónico.
        // Permite actualizar teléfonos, extensiones o contactos
        // sin reiniciar la API.
        // -------------------------------------------------------------
        app.MapPost("/api/phone-directory/reload", (
            PhoneDirectoryStore phoneDirectoryStore) =>
        {
            phoneDirectoryStore.Reload();

            var entries = phoneDirectoryStore.GetEntries();

            return Results.Ok(new
            {
                message = "Directorio telefónico recargado correctamente.",
                enabled = phoneDirectoryStore.Enabled,
                filePath = phoneDirectoryStore.FilePath,
                entryCount = entries.Count
            });
        });

        return app;
    }
}