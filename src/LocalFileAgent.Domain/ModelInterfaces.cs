using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LocalFileAgent.Domain.Models;

public sealed record ChatMessage(
    string Role,
    string Content,
    IReadOnlyList<string>? ImagesBase64 = null
);

public sealed record ChatRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    JsonElement? FormatSchema = null,
    float Temperature = 0.1f,
    string? KeepAlive = "2m"
);

public sealed record ChatResponse(
    string Model,
    string Content,
    long PromptTokens,
    long CompletionTokens,
    double TotalDurationMs
);

public sealed record EmbeddingRequest(
    string Model,
    IReadOnlyList<string> Input,
    string? KeepAlive = "2m"
);

public sealed record EmbeddingResponse(
    string Model,
    IReadOnlyList<float[]> Embeddings,
    long PromptTokens,
    double TotalDurationMs
);

public sealed record GenerateRequest(
    string Model,
    string Prompt,
    IReadOnlyList<string>? ImagesBase64 = null,
    JsonElement? FormatSchema = null,
    float Temperature = 0.1f,
    string? KeepAlive = "2m"
);

public sealed record GenerateResponse(
    string Model,
    string Response,
    long PromptTokens,
    long CompletionTokens,
    double TotalDurationMs
);

public sealed record ModelInfo(
    string Name,
    string ModifiedAt,
    long SizeBytes
);

public interface IOllamaClient
{
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);
    Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default);
    Task<GenerateResponse> GenerateAsync(GenerateRequest request, CancellationToken cancellationToken = default);
    Task<EmbeddingResponse> EmbedAsync(EmbeddingRequest request, CancellationToken cancellationToken = default);
}
