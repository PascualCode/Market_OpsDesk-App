using Asistente.Desktop.Configuration;
using Asistente.Desktop.Infrastructure;
using Asistente.Desktop.Models;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Asistente.Desktop.Services;

/// <summary>
/// Servicio encargado de enviar mensajes al asistente
/// y leer la respuesta en streaming.
///
/// MainWindow conserva la UI.
/// Este servicio conserva:
/// - Construcción del request.
/// - Serialización JSON.
/// - Petición HTTP streaming.
/// - Lectura progresiva de chunks.
/// - Construcción de la respuesta final.
/// </summary>
public sealed class AssistantChatStreamService
{
    private readonly HttpClient _httpClient;
    private readonly AppConfig _config;
    private readonly DesktopClientContextService _desktopClientContextService;

    public AssistantChatStreamService(
        HttpClient httpClient,
        AppConfig config,
        DesktopClientContextService desktopClientContextService)
    {
        _httpClient = httpClient;
        _config = config;
        _desktopClientContextService = desktopClientContextService;
    }

    /// <summary>
    /// Envía una pregunta al asistente y consume la respuesta en streaming.
    ///
    /// onStreamStarted se ejecuta cuando la API responde 2xx
    /// y estamos a punto de comenzar a leer texto.
    ///
    /// onChunkReceived se ejecuta cada vez que llega un fragmento
    /// de respuesta.
    /// </summary>
    public async Task<AssistantChatStreamResult> StreamResponseAsync(
        string question,
        List<ChatHistoryMessage> history,
        Action? onStreamStarted,
        Func<string, Task> onChunkReceived,
        CancellationToken cancellationToken)
    {
        var request = new DesktopToolChatRequest
        {
            Message = question,
            Model = string.IsNullOrWhiteSpace(_config.Model)
                ? null
                : _config.Model,
            History = history,
            Client = _desktopClientContextService.Build()
        };

        var json = JsonSerializer.Serialize(
            request,
            JsonOptions.Default
        );

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"
        );

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            _config.ApiStreamUrl
        )
        {
            Content = content
        };

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        if (!response.IsSuccessStatusCode)
        {
            return new AssistantChatStreamResult
            {
                Success = false,
                ErrorMessage =
                    $"Error consultando al asistente: {(int)response.StatusCode} {response.ReasonPhrase}"
            };
        }

        onStreamStarted?.Invoke();

        await using var stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8
        );

        var finalAnswer = new StringBuilder();
        var buffer = new char[256];

        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await reader.ReadAsync(
                buffer.AsMemory(0, buffer.Length),
                cancellationToken
            );

            if (read <= 0)
                break;

            var chunk = new string(
                buffer,
                0,
                read
            );

            finalAnswer.Append(chunk);

            await onChunkReceived(chunk);
        }

        return new AssistantChatStreamResult
        {
            Success = true,
            Answer = finalAnswer.ToString()
        };
    }
}