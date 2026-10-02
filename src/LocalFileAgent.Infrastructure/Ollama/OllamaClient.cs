using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LocalFileAgent.Domain.Models;

namespace LocalFileAgent.Infrastructure.Ollama;

public sealed class OllamaClient : IOllamaClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OllamaClient(HttpClient httpClient, string endpoint = "http://127.0.0.1:11434")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        var uri = new Uri(endpoint);

        ValidateLoopback(uri);

        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUri = uri;
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var targetUri = new Uri(_baseUri, "/api/version");
        ValidateLoopback(targetUri);

        using var response = await _httpClient.GetAsync(targetUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        return doc.RootElement.GetProperty("version").GetString() ?? string.Empty;
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var targetUri = new Uri(_baseUri, "/api/tags");
        ValidateLoopback(targetUri);

        using var response = await _httpClient.GetAsync(targetUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var list = new List<ModelInfo>();
        if (doc.RootElement.TryGetProperty("models", out var modelsElem) && modelsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in modelsElem.EnumerateArray())
            {
                var name = item.GetProperty("name").GetString() ?? string.Empty;
                var modifiedAt = item.TryGetProperty("modified_at", out var modProp) ? modProp.GetString() ?? string.Empty : string.Empty;
                var size = item.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt64() : 0L;
                list.Add(new ModelInfo(name, modifiedAt, size));
            }
        }

        return list;
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var targetUri = new Uri(_baseUri, "/api/chat");
        ValidateLoopback(targetUri);

        var messagesPayload = new List<Dictionary<string, object>>();
        foreach (var msg in request.Messages)
        {
            var msgDict = new Dictionary<string, object>
            {
                ["role"] = msg.Role,
                ["content"] = msg.Content
            };
            if (msg.ImagesBase64 is { Count: > 0 })
            {
                msgDict["images"] = msg.ImagesBase64;
            }
            messagesPayload.Add(msgDict);
        }

        var payload = new Dictionary<string, object>
        {
            ["model"] = request.Model,
            ["messages"] = messagesPayload,
            ["stream"] = false,
            ["options"] = new Dictionary<string, object>
            {
                ["temperature"] = request.Temperature
            }
        };

        if (request.KeepAlive is not null)
        {
            payload["keep_alive"] = request.KeepAlive;
        }

        if (request.FormatSchema.HasValue)
        {
            payload["format"] = request.FormatSchema.Value;
        }

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(targetUri, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var root = doc.RootElement;
        var model = root.TryGetProperty("model", out var mProp) ? mProp.GetString() ?? request.Model : request.Model;
        var messageContent = string.Empty;
        if (root.TryGetProperty("message", out var msgObj) && msgObj.TryGetProperty("content", out var cProp))
        {
            messageContent = cProp.GetString() ?? string.Empty;
        }

        var promptTokens = root.TryGetProperty("prompt_eval_count", out var peProp) ? peProp.GetInt64() : 0L;
        var completionTokens = root.TryGetProperty("eval_count", out var eProp) ? eProp.GetInt64() : 0L;
        var totalDurationNanos = root.TryGetProperty("total_duration", out var tdProp) ? tdProp.GetInt64() : 0L;

        return new ChatResponse(
            Model: model,
            Content: messageContent,
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens,
            TotalDurationMs: totalDurationNanos / 1_000_000.0
        );
    }

    public async Task<EmbeddingResponse> EmbedAsync(EmbeddingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var targetUri = new Uri(_baseUri, "/api/embed");
        ValidateLoopback(targetUri);

        var payload = new Dictionary<string, object>
        {
            ["model"] = request.Model,
            ["input"] = request.Input
        };

        if (request.KeepAlive is not null)
        {
            payload["keep_alive"] = request.KeepAlive;
        }

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(targetUri, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var root = doc.RootElement;
        var model = root.TryGetProperty("model", out var mProp) ? mProp.GetString() ?? request.Model : request.Model;
        var promptTokens = root.TryGetProperty("prompt_eval_count", out var peProp) ? peProp.GetInt64() : 0L;
        var totalDurationNanos = root.TryGetProperty("total_duration", out var tdProp) ? tdProp.GetInt64() : 0L;

        var embeddingsList = new List<float[]>();
        if (root.TryGetProperty("embeddings", out var embArray) && embArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var vectorElem in embArray.EnumerateArray())
            {
                var vec = new float[vectorElem.GetArrayLength()];
                var idx = 0;
                foreach (var floatElem in vectorElem.EnumerateArray())
                {
                    vec[idx++] = floatElem.GetSingle();
                }
                embeddingsList.Add(vec);
            }
        }

        return new EmbeddingResponse(
            Model: model,
            Embeddings: embeddingsList,
            PromptTokens: promptTokens,
            TotalDurationMs: totalDurationNanos / 1_000_000.0
        );
    }

    private static void ValidateLoopback(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        var isLoopback = uri.IsLoopback ||
                         host == "127.0.0.1" ||
                         host == "::1" ||
                         host == "[::1]" ||
                         host == "localhost";

        if (!isLoopback)
        {
            throw new InvalidOperationException(
                $"Security policy violation: Endpoint '{uri}' is not loopback. LocalFileAgent strictly permits only 127.0.0.1 / ::1 / localhost."
            );
        }
    }
}
