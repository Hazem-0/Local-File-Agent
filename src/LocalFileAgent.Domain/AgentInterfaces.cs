using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalFileAgent.Domain.Search;

namespace LocalFileAgent.Domain.Agent;

public sealed record SearchPlan(
    string RawQuery,
    string NormalizedQuery,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> DialectVariants,
    IReadOnlyList<string>? ScopePaths = null,
    IReadOnlyList<string>? FileExtensions = null,
    string Intent = "search_files",
    int Limit = 20
);

public sealed record GroundingValidationResult(
    bool IsValid,
    IReadOnlyList<string> HallucinatedPaths,
    IReadOnlyList<SearchResultItem> VerifiedCitations,
    string SanitizedResponseText
);

public sealed record AgentAnswer(
    string UserQuery,
    string ResponseText,
    SearchPlan Plan,
    IReadOnlyList<SearchResultItem> Hits,
    IReadOnlyList<SearchResultItem> VerifiedCitations,
    bool IsGrounded,
    TimeSpan Elapsed
);

public interface IAgentPlanner
{
    Task<SearchPlan> PlanAsync(
        string userQuery,
        IReadOnlyList<string>? scopePaths = null,
        CancellationToken cancellationToken = default
    );
}

public interface IGroundingValidator
{
    GroundingValidationResult Validate(
        string generatedText,
        IReadOnlyList<SearchResultItem> toolHits
    );
}

public interface IAgentService
{
    Task<AgentAnswer> AskAsync(
        string userQuery,
        IReadOnlyList<string>? scopePaths = null,
        CancellationToken cancellationToken = default
    );
}

public enum FileChangeKind
{
    Created,
    Changed,
    Deleted,
    Renamed
}

public sealed record FileChangeEvent(
    FileChangeKind Kind,
    string FullPath,
    string? OldFullPath = null
);

public interface IFileWatcherService : IDisposable
{
    void StartWatching(IEnumerable<string> directoryPaths);
    void StopWatching();
    bool IsWatching { get; }
    IReadOnlyList<string> WatchedPaths { get; }
    event Func<FileChangeEvent, Task>? FileChanged;
}
