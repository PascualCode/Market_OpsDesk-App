using Asistente.Api.Configuration;
using Asistente.Api.Posters;
using PdfSharp.Fonts;
using System.Diagnostics;
using static Asistente.Api.Posters.PosterPriceTypeParser;
using Asistente.Api.DesktopTasks;

// =============================================================
// Asistente.Api
// -------------------------------------------------------------
// API intermedia entre la aplicación de escritorio del avatar
// y el servidor de modelos local gestionado por Ollama.
//
// Flujo esperado:
// App escritorio -> Asistente.Api -> Ollama -> Modelo IA
//
// Esta API evita que la aplicación de escritorio tenga que hablar
// directamente con Ollama. Así podemos centralizar configuración,
// modelo, prompt del sistema, seguridad y futuras herramientas.
// =============================================================

var builder = WebApplication.CreateBuilder(args);


// -------------------------------------------------------------
// Comprobacion necesaria para la fuente usada en la generacion de PDFs
// =============================================================
if (GlobalFontSettings.FontResolver is null)
{
    GlobalFontSettings.FontResolver = new DejaVuFontResolver();
}

// -------------------------------------------------------------
// LOGGING
// -------------------------------------------------------------
builder.Services.AddSingleton<ApiFileLogger>();
builder.Services.AddSingleton<ToolFileLogger>();
builder.Services.AddSingleton<OllamaPerformanceFileLogger>();


//SERVICIOS CARTELES
builder.Services.Configure<PosterDatabaseOptions>(
    builder.Configuration.GetSection("PosterDatabase")
);
builder.Services.AddSingleton<PosterProductRepository>();
builder.Services.AddSingleton<PosterPdfRenderService>();
builder.Services.AddSingleton<LabelRepository>();
builder.Services.AddSingleton<LabelPdfRenderService>();

//SERVICIOS TAREAS
builder.Services.Configure<DesktopTaskOptions>(
    builder.Configuration.GetSection("DesktopTasks")
);

builder.Services.AddSingleton<DesktopTaskRepository>();

builder.Services.AddSingleton<KnowledgeStore>();
builder.Services.AddSingleton<SemanticKnowledgeIndexer>();
builder.Services.AddSingleton<SemanticKnowledgeSearcher>();
builder.Services.AddSingleton<KnowledgeContextService>();
builder.Services.AddSingleton<PhoneDirectoryStore>();
builder.Services.AddSingleton<PlanningRepository>();
builder.Services.AddSingleton<LocalActionRepository>();

builder.Services.AddSingleton<IAssistantServerTool, GetServerDateTimeTool>();

builder.Services.AddSingleton<IAssistantServerTool, CreateTaskTool>();
builder.Services.AddSingleton<IAssistantServerTool, ListTasksTool>();
builder.Services.AddSingleton<IAssistantServerTool, CompleteTaskTool>();
builder.Services.AddSingleton<IAssistantServerTool, DeleteTaskTool>();
builder.Services.AddSingleton<IAssistantServerTool, DeleteAllTasksTool>();

builder.Services.AddSingleton<IAssistantServerTool, CreateReminderTool>();
builder.Services.AddSingleton<IAssistantServerTool, ListRemindersTool>();
builder.Services.AddSingleton<IAssistantServerTool, DeleteReminderTool>();
builder.Services.AddSingleton<IAssistantServerTool, DeleteAllRemindersTool>();
builder.Services.AddSingleton<IAssistantServerTool, SearchPhoneDirectoryTool>();

builder.Services.AddSingleton<IAssistantServerTool, RequestCloseLocalProgramTool>();
builder.Services.AddSingleton<IAssistantServerTool, RequestOpenLocalProgramTool>();
builder.Services.AddSingleton<IAssistantServerTool, RequestOpenLocalFolderTool>();

builder.Services.Configure<PosterRenderOptions>(
    builder.Configuration.GetSection("PosterRender")
);

builder.Services.AddSingleton<AssistantToolOrchestrator>();

// -------------------------------------------------------------
// Configuración CORS
// -------------------------------------------------------------
// Permite que clientes externos, como una futura app de escritorio
// o una interfaz web interna, puedan llamar a esta API.
//
// Durante desarrollo se permite cualquier origen.
// En producción conviene limitarlo a los equipos/redes necesarias.
// -------------------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowDesktopClient", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// -------------------------------------------------------------
// Cliente HTTP para Ollama
// -------------------------------------------------------------
// La URL base se lee de appsettings.json:
//
// "Ollama": {
//   "BaseUrl": ""
// }
//
// Si esta API se ejecuta en el servidor-ia, normalmente usaremos
// la IP/host donde Ollama escucha.
// -------------------------------------------------------------
builder.Services.AddHttpClient("Ollama", client =>
{
    var baseUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";

    client.BaseAddress = new Uri(baseUrl);

    // Los modelos locales pueden tardar en responder, sobre todo en CPU.
    client.Timeout = TimeSpan.FromMinutes(10);
});

builder.Services.AddHttpClient("Qdrant", client =>
{
    var baseUrl = builder.Configuration["Qdrant:BaseUrl"]
        ?? "http://127.0.0.1:6333";

    client.BaseAddress = new Uri(baseUrl);

    // Los modelos locales pueden tardar en responder, sobre todo en CPU.
    client.Timeout = TimeSpan.FromMinutes(10);
});

var app = builder.Build();

app.UseCors("AllowDesktopClient");

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ApiFileLogger>();
    var stopwatch = Stopwatch.StartNew();

    string? error = null;

    try
    {
        await next();
    }
    catch (Exception ex)
    {
        error = ex.GetType().Name + ": " + ex.Message;
        throw;
    }
    finally
    {
        stopwatch.Stop();

        var model = context.Items.TryGetValue("Model", out var modelValue)
            ? modelValue?.ToString()
            : null;

        var questionLength = context.Items.TryGetValue("QuestionLength", out var lengthValue) &&
                             int.TryParse(lengthValue?.ToString(), out var parsedLength)
            ? parsedLength
            : (int?)null;

        await logger.LogAsync(new ApiLogEntry
        {
            Date = DateTime.Now,
            ClientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            Method = context.Request.Method,
            Path = context.Request.Path,
            StatusCode = context.Response.StatusCode,
            Model = model,
            QuestionLength = questionLength,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            Error = error
        });
    }
});

// -------------------------------------------------------------
// GET /api/knowledge/status
// -------------------------------------------------------------
// Devuelve el estado actual de la base de conocimiento:
// - Si está habilitada.
// - Carpeta configurada.
// - Número de documentos cargados.
// - Lista resumida de documentos.
// -------------------------------------------------------------
app.MapGet("/api/knowledge/status", (KnowledgeStore knowledgeStore) =>
{
    var documents = knowledgeStore.GetDocuments();

    return Results.Ok(new
    {
        enabled = knowledgeStore.Enabled,
        directory = knowledgeStore.Directory,
        documentCount = documents.Count,
        documents = documents.Select(document => new
        {
            path = document.RelativePath,
            title = document.Title,
            category = document.Category,
            characters = document.Content.Length,
            lastModifiedUtc = document.LastModifiedUtc
        })
    });
});

app.MapGet("/api/posters/products/{code}", async (
    string code,
    PosterProductRepository repository,
    CancellationToken cancellationToken) =>
{
    var result = await repository.FindByCodeAsync(
        code,
        cancellationToken
    );

    return Results.Ok(result);
});

//****** PRUEBAS CARTELES *********
/*
app.MapGet("/api/posters/generate-test/{code}", async (
    string code,
    PosterProductRepository repository,
    PosterPdfRenderService pdfRenderService,
    CancellationToken cancellationToken) =>
{
    var result = await repository.FindByCodeAsync(
        code,
        cancellationToken
    );

    if (!result.Found || result.Product is null)
    {
        return Results.NotFound(result);
    }

    var pdfBytes =
        pdfRenderService.RenderPoster(
            result.Product,
            posterSize,
            posterPriceType
        );

    var fileName =
        $"cartel-{result.Product.Code}.pdf";

    return Results.File(
        pdfBytes,
        "application/pdf",
        fileName
    );
});
*/

app.MapGet("/api/posters/generate/{code}", async (
    string code,
    string? size,
    string? priceType,
    PosterProductRepository repository,
    PosterPdfRenderService pdfRenderService,
    CancellationToken cancellationToken) =>
{
    var posterSize =
        PosterSizeParser.ParseOrDefault(
            size,
            PosterSize.A4
        );

    var posterPriceType =
        PosterPriceTypeParser.ParseOrDefault(
            priceType,
            PosterPriceType.Normal
        );

    var result = await repository.FindByCodeAsync(
        code,
        cancellationToken
    );

    if (!result.Found || result.Product is null)
    {
        return Results.NotFound(result);
    }

    var pdfBytes =
        pdfRenderService.RenderPoster(
            result.Product,
            posterSize,
            posterPriceType
        );

    var fileName =
        $"cartel-{result.Product.Code}-{posterSize}-{posterPriceType}.pdf";

    return Results.File(
        pdfBytes,
        "application/pdf",
        fileName
    );
});

/*
 * Endpoint de búsqueda de productos para el generador de carteles.
 *
 * Permite al Desktop buscar productos antes de añadirlos
 * al listado de carteles a generar.
 *
 * Parámetros:
 * - query: texto a buscar.
 * - type: tipo de búsqueda. Puede ser Ean, ArticleCode o Name.
 * - maxResults: máximo de productos a devolver.
 */
app.MapGet("/api/posters/products/search", async (
    string query,
    string? type,
    int? maxResults,
    PosterProductRepository repository,
    CancellationToken cancellationToken) =>
{
    var searchType =
        PosterProductSearchTypeParser.ParseOrDefault(
            type,
            PosterProductSearchType.Ean
        );

    var products =
        await repository.SearchAsync(
            query,
            searchType,
            maxResults ?? 20,
            cancellationToken
        );

    return Results.Ok(new
    {
        found = products.Count > 0,
        count = products.Count,
        products
    });
});

/*
 * Endpoint multiartículo para generación de carteles.
 *
 * Recibe una lista de códigos internos de artículo y genera
 * un único PDF con todos ellos colocados según el tamaño elegido.
 *
 * A3/A4: 1 producto por hoja.
 * A5: 2 productos por hoja.
 * A6: 4 productos por hoja.
 */
app.MapPost("/api/posters/generate-batch", async (
    PosterBatchGenerationRequest request,
    PosterProductRepository repository,
    PosterPdfRenderService pdfRenderService,
    CancellationToken cancellationToken) =>
{
    if (request.Items is null ||
        request.Items.Count == 0)
    {
        return Results.BadRequest(new
        {
            message = "No se ha indicado ningún artículo para generar carteles."
        });
    }

    var productCodes =
        request.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.ProductCode))
            .Select(item => item.ProductCode.Trim())
            .ToList();

    if (productCodes.Count == 0)
    {
        return Results.BadRequest(new
        {
            message = "No hay códigos de artículo válidos."
        });
    }

    var products =
        await repository.FindByArticleCodesAsync(
            productCodes,
            cancellationToken
        );

    if (products.Count == 0)
    {
        return Results.NotFound(new
        {
            message = "No se ha encontrado ningún producto válido."
        });
    }

    var renderItems = new List<PosterRenderItem>();

    for (var i = 0; i < products.Count && i < request.Items.Count; i++)
    {
        var requestItem =
            request.Items[i];

        renderItems.Add(new PosterRenderItem
        {
            Product = products[i],
            Size = PosterSizeParser.ParseOrDefault(
                requestItem.Size,
                PosterSize.A4
            ),
            PriceType = PosterPriceTypeParser.ParseOrDefault(
                requestItem.PriceType,
                PosterPriceType.Normal
            )
        });
    }

    var pdfBytes =
        pdfRenderService.RenderMixedPosters(
            renderItems
        );

    var fileName =
        $"carteles-mixto-{DateTime.Now:yyyyMMddHHmmss}.pdf";

    return Results.File(
        pdfBytes,
        "application/pdf",
        fileName
    );
});

/*
 * Endpoint de etiquetas pendientes.
 *
 * Devuelve las etiquetas pendientes de impresión del año actual.
 * De momento solo lectura.
 *
 * Ejemplo:
 * GET /api/labels/pending?warehouse=01&maxResults=500
 */
app.MapGet("/api/labels/pending", async (
    string? warehouse,
    int? maxResults,
    LabelRepository repository,
    CancellationToken cancellationToken) =>
{
    var labels =
        await repository.GetPendingLabelsForCurrentYearAsync(
            string.IsNullOrWhiteSpace(warehouse) ? "01" : warehouse,
            maxResults ?? 500,
            cancellationToken
        );

    return Results.Ok(new PendingLabelsResponse
    {
        Found = labels.Count > 0,
        Count = labels.Count,
        Labels = labels
    });
});

/*
 * Genera un PDF con las etiquetas seleccionadas.
 *
 * De momento NO marca como impresas.
 * Solo genera PDF para previsualizar/imprimir.
 */
app.MapPost("/api/labels/generate", async (
    LabelBatchGenerationRequest request,
    LabelRepository repository,
    LabelPdfRenderService labelPdfRenderService,
    CancellationToken cancellationToken) =>
{
    if (request.RowIds is null ||
        request.RowIds.Count == 0)
    {
        return Results.BadRequest(new
        {
            message = "No se ha indicado ninguna etiqueta para generar."
        });
    }

    var labels =
        await repository.GetLabelsByRowIdsAsync(
            request.RowIds,
            cancellationToken
        );

    if (labels.Count == 0)
    {
        return Results.NotFound(new
        {
            message = "No se ha encontrado ninguna etiqueta válida."
        });
    }

    var labelFormat =
        LabelFormatParser.ParseOrDefault(
            request.Format,
            LabelFormat.Normal
        );

    var pdfBytes =
        labelPdfRenderService.RenderLabels(
            labels,
            labelFormat
        );

    var fileName =
        $"etiquetas-{labelFormat}-{DateTime.Now:yyyyMMddHHmmss}.pdf";

    return Results.File(
        pdfBytes,
        "application/pdf",
        fileName
    );
});

/*
 * Endpoint de búsqueda de etiquetas para reposición.
 *
 * Permite buscar etiquetas por:
 * - EAN
 * - Código artículo
 * - Nombre
 *
 * No filtra por Impreso = 0.
 */
app.MapGet("/api/labels/search", async (
    string query,
    string? type,
    string? warehouse,
    int? maxResults,
    LabelRepository repository,
    CancellationToken cancellationToken) =>
{
    var searchType =
        PosterProductSearchTypeParser.ParseOrDefault(
            type,
            PosterProductSearchType.Ean
        );

    var labels =
        await repository.SearchLabelsForRestockAsync(
            query,
            searchType,
            string.IsNullOrWhiteSpace(warehouse) ? "01" : warehouse,
            maxResults ?? 100,
            cancellationToken
        );

    return Results.Ok(new PendingLabelsResponse
    {
        Found = labels.Count > 0,
        Count = labels.Count,
        Labels = labels
    });
});

/*
 * Marca etiquetas como impresas.
 *
 * Se llamará desde Desktop solo después de enviar la orden de impresión
 * y solo si el checkbox está marcado.
 */
app.MapPost("/api/labels/mark-printed", async (
    MarkLabelsAsPrintedRequest request,
    LabelRepository repository,
    CancellationToken cancellationToken) =>
{
    if (request.RowIds is null ||
        request.RowIds.Count == 0)
    {
        return Results.BadRequest(new MarkLabelsAsPrintedResult
        {
            Success = false,
            RequestedCount = 0,
            UpdatedCount = 0,
            Message = "No se ha indicado ninguna etiqueta para marcar como impresa."
        });
    }

    var updatedCount =
        await repository.MarkLabelsAsPrintedAsync(
            request.RowIds,
            request.User,
            cancellationToken
        );

    return Results.Ok(new MarkLabelsAsPrintedResult
    {
        Success = updatedCount > 0,
        RequestedCount = request.RowIds.Count,
        UpdatedCount = updatedCount,
        Message = $"Etiquetas marcadas como impresas: {updatedCount}."
    });
});

/*
 * Devuelve los grupos de tareas a los que pertenece el usuario/equipo.
 */
app.MapPost("/api/desktop-tasks/groups", (
    DesktopTaskGroupsRequest request,
    DesktopTaskRepository repository) =>
{
    var groups =
        repository.GetGroupsForClient(
            request.Client
        );

    return Results.Ok(new DesktopTaskGroupsResponse
    {
        Count = groups.Count,
        Groups = groups
    });
});

/*
 * Lista las tareas/recordatorios visibles para el usuario/equipo.
 */
app.MapPost("/api/desktop-tasks/visible", async (
    DesktopTasksForClientRequest request,
    DesktopTaskRepository repository,
    CancellationToken cancellationToken) =>
{
    var tasks =
        await repository.GetVisibleAsync(
            request.Client,
            cancellationToken
        );

    return Results.Ok(new DesktopTasksResponse
    {
        Count = tasks.Count,
        Tasks = tasks
    });
});

/*
 * Crea una tarea/recordatorio desde Desktop.
 */
app.MapPost("/api/desktop-tasks", async (
    CreateDesktopTaskRequest request,
    DesktopTaskRepository repository,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.WrittenBy))
    {
        return Results.BadRequest(new
        {
            message = "El campo 'Escrito por' es obligatorio."
        });
    }

    if (string.IsNullOrWhiteSpace(request.AssignedTo))
    {
        return Results.BadRequest(new
        {
            message = "El campo 'Dirigido a' es obligatorio."
        });
    }

    if (string.IsNullOrWhiteSpace(request.Description))
    {
        return Results.BadRequest(new
        {
            message = "La descripción es obligatoria."
        });
    }

    if (request.DueAt == default)
    {
        return Results.BadRequest(new
        {
            message = "La fecha de vencimiento es obligatoria."
        });
    }

    if (string.IsNullOrWhiteSpace(request.GroupName))
    {
        return Results.BadRequest(new
        {
            message = "El grupo destino es obligatorio."
        });
    }

    try
    {
        var createdTask =
            await repository.CreateAsync(
                request,
                cancellationToken
            );

        return Results.Ok(createdTask);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new
        {
            message = ex.Message
        });
    }
});

/*
 * Elimina una o varias tareas seleccionadas desde Desktop.
 */
app.MapPost("/api/desktop-tasks/delete", async (
    DeleteDesktopTasksRequest request,
    DesktopTaskRepository repository,
    CancellationToken cancellationToken) =>
{
    if (request.TaskIds is null ||
        request.TaskIds.Count == 0)
    {
        return Results.BadRequest(new DeleteDesktopTasksResult
        {
            Success = false,
            RequestedCount = 0,
            DeletedCount = 0,
            Message = "No se ha indicado ninguna tarea para eliminar."
        });
    }

    var deletedCount =
        await repository.DeleteAsync(
            request.TaskIds,
            request.Client,
            cancellationToken
        );

    return Results.Ok(new DeleteDesktopTasksResult
    {
        Success = deletedCount > 0,
        RequestedCount = request.TaskIds.Count,
        DeletedCount = deletedCount,
        Message = $"Tareas eliminadas: {deletedCount}."
    });
});

/*
 * Aplaza la fecha límite de una tarea.
 */
app.MapPost("/api/desktop-tasks/postpone", async (
    PostponeDesktopTaskRequest request,
    DesktopTaskRepository repository,
    CancellationToken cancellationToken) =>
{
    var result =
        await repository.PostponeAsync(
            request,
            cancellationToken
        );

    if (!result.Success)
    {
        return Results.BadRequest(result);
    }

    return Results.Ok(result);
});

/*
 * Actualiza la descripción de una tarea.
 */
app.MapPost("/api/desktop-tasks/update-description", async (
    UpdateDesktopTaskDescriptionRequest request,
    DesktopTaskRepository repository,
    CancellationToken cancellationToken) =>
{
    var result =
        await repository.UpdateDescriptionAsync(
            request,
            cancellationToken
        );

    if (!result.Success)
    {
        return Results.BadRequest(result);
    }

    return Results.Ok(result);
});

/*
 * Sube un archivo adjunto y lo vincula a una tarea.
 */
app.MapPost("/api/desktop-tasks/attachments/upload", async (
    HttpRequest request,
    DesktopTaskRepository repository,
    CancellationToken cancellationToken) =>
{
    var form =
        await request.ReadFormAsync(cancellationToken);

    var file =
        form.Files["file"];

    if (file is null)
    {
        return Results.BadRequest(new UploadDesktopTaskAttachmentResult
        {
            Success = false,
            Message = "No se ha recibido ningún archivo."
        });
    }

    if (!Guid.TryParse(form["taskId"], out var taskId))
    {
        return Results.BadRequest(new UploadDesktopTaskAttachmentResult
        {
            Success = false,
            Message = "El identificador de tarea no es válido."
        });
    }

    var client =
        new DesktopTaskClientDto
        {
            UserName = form["userName"].ToString(),
            DomainName = form["domainName"].ToString(),
            MachineName = form["machineName"].ToString(),
            DisplayName = form["displayName"].ToString()
        };

    await using var stream =
        file.OpenReadStream();

    var result =
        await repository.UploadAttachmentAsync(
            client,
            taskId,
            file.FileName,
            stream,
            file.Length,
            cancellationToken
        );

    if (!result.Success)
    {
        return Results.BadRequest(result);
    }

    return Results.Ok(result);
})
.DisableAntiforgery();
/*
 * Descarga un archivo adjunto de una tarea.
 */
app.MapPost("/api/desktop-tasks/attachments/download", async (
    DownloadDesktopTaskAttachmentRequest request,
    DesktopTaskRepository repository,
    CancellationToken cancellationToken) =>
{
    var result =
        await repository.GetAttachmentFileAsync(
            request,
            cancellationToken
        );

    if (!result.Success)
    {
        return Results.BadRequest(new
        {
            success = false,
            message = result.Message
        });
    }

    return Results.File(
        result.FullPath,
        "application/octet-stream",
        result.FileName
    );
});

/*
 * Elimina un archivo adjunto de una tarea.
 */
app.MapPost("/api/desktop-tasks/attachments/delete", async (
    DeleteDesktopTaskAttachmentRequest request,
    DesktopTaskRepository repository,
    CancellationToken cancellationToken) =>
{
    var result =
        await repository.DeleteAttachmentAsync(
            request,
            cancellationToken
        );

    if (!result.Success)
    {
        return Results.BadRequest(result);
    }

    return Results.Ok(result);
});


app.MapCoreEndpoints();
app.MapChatEndpoints();
app.MapToolEndpoints();
app.MapKnowledgeEndpoints();
app.MapRagEndpoints();
app.MapReminderEndpoints();
app.MapPhoneDirectoryEndpoints();
app.MapLocalActionEndpoints();

app.Run();