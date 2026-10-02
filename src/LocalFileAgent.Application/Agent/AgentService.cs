using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LocalFileAgent.Domain.Agent;
using LocalFileAgent.Domain.Models;
using LocalFileAgent.Domain.Search;
using LocalFileAgent.Text;

namespace LocalFileAgent.Application.Agent;

public sealed record AgentServiceOptions
{
    public string ModelName { get; init; } = "gemma4:e2b";
    public float Temperature { get; init; } = 0.2f;
    public int MaxHitsToCite { get; init; } = 5;
    public int TimeoutMs { get; init; } = 5000;
}

public sealed class AgentService : IAgentService
{
    private readonly IAgentPlanner _planner;
    private readonly ISearchService _searchService;
    private readonly IGroundingValidator _validator;
    private readonly IOllamaClient? _ollamaClient;
    private readonly AgentServiceOptions _options;

    public AgentService(
        IAgentPlanner planner,
        ISearchService searchService,
        IGroundingValidator? validator = null,
        IOllamaClient? ollamaClient = null,
        AgentServiceOptions? options = null)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _validator = validator ?? new GroundingValidator();
        _ollamaClient = ollamaClient;
        _options = options ?? new AgentServiceOptions();
    }

    public async Task<AgentAnswer> AskAsync(
        string userQuery,
        IReadOnlyList<string>? scopePaths = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userQuery);

        var sw = Stopwatch.StartNew();

        // Step 1: Call A - Search Planning & Query Translation
        var plan = await _planner.PlanAsync(userQuery, scopePaths, cancellationToken).ConfigureAwait(false);

        // Step 2: Deterministic Tool Execution
        var hits = await ExecuteSearchPlanAsync(plan, cancellationToken).ConfigureAwait(false);

        // Step 3: Call B - Grounded Answer Synthesis
        string rawResponse;
        if (hits.Count == 0)
        {
            rawResponse = "لم يتم العثور على أي ملفات مطابقة لبحثك في المسارات المحددة.";
        }
        else
        {
            rawResponse = await SynthesizeAnswerAsync(userQuery, hits, cancellationToken).ConfigureAwait(false);
        }

        // Step 4: Grounding & Hallucination Validation
        var validation = _validator.Validate(rawResponse, hits);

        sw.Stop();

        return new AgentAnswer(
            UserQuery: userQuery,
            ResponseText: validation.SanitizedResponseText,
            Plan: plan,
            Hits: hits,
            VerifiedCitations: validation.VerifiedCitations.Count > 0 ? validation.VerifiedCitations : hits.Take(_options.MaxHitsToCite).ToList(),
            IsGrounded: validation.IsValid,
            Elapsed: sw.Elapsed
        );
    }

    private async Task<IReadOnlyList<SearchResultItem>> ExecuteSearchPlanAsync(SearchPlan plan, CancellationToken cancellationToken)
    {
        // 1. Primary search with planned normalized query
        var req = new SearchRequest(
            Query: plan.NormalizedQuery,
            ScopePaths: plan.ScopePaths,
            Limit: plan.Limit
        );

        var hits = await _searchService.SearchAsync(req, cancellationToken).ConfigureAwait(false);

        // 2. If 0 hits and we have dialect variants or keywords, try fallback variant
        if (hits.Count == 0 && plan.DialectVariants.Count > 0)
        {
            var fallbackQuery = string.Join(" ", plan.DialectVariants);
            var fallbackReq = new SearchRequest(
                Query: fallbackQuery,
                ScopePaths: plan.ScopePaths,
                Limit: plan.Limit
            );
            hits = await _searchService.SearchAsync(fallbackReq, cancellationToken).ConfigureAwait(false);
        }

        // 3. Filter by extension if explicitly requested by plan
        if (plan.FileExtensions != null && plan.FileExtensions.Count > 0 && hits.Count > 0)
        {
            var filtered = hits.Where(h =>
            {
                var ext = System.IO.Path.GetExtension(h.FilePath);
                return plan.FileExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
            }).ToList();

            if (filtered.Count > 0)
            {
                return filtered;
            }
        }

        return hits;
    }

    private async Task<string> SynthesizeAnswerAsync(
        string userQuery,
        IReadOnlyList<SearchResultItem> hits,
        CancellationToken cancellationToken)
    {
        var topHits = hits.Take(_options.MaxHitsToCite).ToList();

        if (_ollamaClient != null)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_options.TimeoutMs);

                var promptBuilder = new StringBuilder();
                promptBuilder.AppendLine("أنت وكيل بحث محلي ذكي وموثوق (LocalFileAgent).");
                promptBuilder.AppendLine("المهمة: أجب على استفسار المستخدم باللغة العربية بناءً فقط وحصراً على نتائج البحث المرفقة.");
                promptBuilder.AppendLine("القواعد الصارمة:");
                promptBuilder.AppendLine("1. اذكر أسماء الملفات وأرقام الصفحات بدقة واضحة.");
                promptBuilder.AppendLine("2. لا تذكر أو تختلق أية أسماء ملفات أو مسارات لم ترد في النتائج أدناه.");
                promptBuilder.AppendLine("3. ضع كل اسم ملف أو مسار بين علامتي اقتباس.");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"استفسار المستخدم: {userQuery}");
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("نتائج البحث المتوفرة:");

                for (var i = 0; i < topHits.Count; i++)
                {
                    var h = topHits[i];
                    var safePath = BidiHelper.WrapLtrIsolate(h.FilePath);
                    promptBuilder.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"[{i + 1}] اسم الملف: \"{h.FileName}\" (الصفحة {h.PageNumber})");
                    promptBuilder.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"    المسار: {safePath}");
                    promptBuilder.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"    المقتطف: {h.Snippet}");
                }

                var req = new ChatRequest(
                    Model: _options.ModelName,
                    Messages: new[]
                    {
                        new ChatMessage("user", promptBuilder.ToString())
                    },
                    Temperature: _options.Temperature,
                    KeepAlive: "2m"
                );

                var resp = await _ollamaClient.ChatAsync(req, cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(resp.Content))
                {
                    return resp.Content.Trim();
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout: fallback to deterministic synthesizer
            }
            catch
            {
                // LLM failure: fallback to deterministic synthesizer
            }
        }

        // Deterministic answer synthesizer fallback
        var sb = new StringBuilder();
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"تم العثور على {hits.Count} من الملفات والمستندات المطابقة لبحثك:");
        sb.AppendLine();

        for (var i = 0; i < topHits.Count; i++)
        {
            var h = topHits[i];
            var pageInfo = h.PageNumber > 0 ? $" (صفحة {h.PageNumber})" : string.Empty;
            var safePath = BidiHelper.WrapLtrIsolate(h.FilePath);
            var cleanSnippet = CleanSnippetForAgentText(h.Snippet);

            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"[{i + 1}] \"{h.FileName}\"{pageInfo}:");
            if (!string.IsNullOrWhiteSpace(cleanSnippet))
            {
                sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"    {cleanSnippet}");
            }
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"    المسار: {safePath}");
            sb.AppendLine();
        }

        sb.AppendLine("💡 جميع الملفات مدرجة أدناه كبطاقات تفاعلية — انقر على أي مسار أو زر فتح لتشغيل الملف فوراً.");
        return sb.ToString().TrimEnd();
    }

    private static string CleanSnippetForAgentText(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet)) return string.Empty;
        var text = snippet
            .Replace("**وصف محتوى الصورة:**", "وصف المحتوى: ")
            .Replace("**نوع المستند:**", "نوع المستند: ")
            .Replace("**العناصر البصرية والنصوص الرئيسية:**", "العناصر الرئيسية: ")
            .Replace("**", string.Empty)
            .Replace("###", string.Empty)
            .Replace("##", string.Empty)
            .Replace("#", string.Empty)
            .Trim();

        // Collapse whitespace / multiple newlines
        while (text.Contains("\n\n", StringComparison.Ordinal))
        {
            text = text.Replace("\n\n", " ", StringComparison.Ordinal);
        }

        if (text.Length > 250)
        {
            text = text[..250] + "...";
        }

        return text;
    }
}
