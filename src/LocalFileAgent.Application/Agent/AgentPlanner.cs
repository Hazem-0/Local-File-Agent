using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LocalFileAgent.Domain.Agent;
using LocalFileAgent.Domain.Models;
using LocalFileAgent.Domain.Text;
using LocalFileAgent.Text;

namespace LocalFileAgent.Application.Agent;

public sealed record AgentPlannerOptions
{
    public string ModelName { get; init; } = "gemma4:e2b";
    public float Temperature { get; init; } = 0.1f;
    public int TimeoutMs { get; init; } = 3000;
}

public sealed class AgentPlanner : IAgentPlanner
{
    private readonly IOllamaClient? _ollamaClient;
    private readonly ITextNormalizer _normalizer;
    private readonly IArabicStemmer _stemmer;
    private readonly QueryAnalyzer _analyzer;
    private readonly AgentPlannerOptions _options;

    private static readonly Dictionary<string, string[]> ExtensionMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        { "pdf", new[] { ".pdf" } },
        { "بي دي اف", new[] { ".pdf" } },
        { "word", new[] { ".docx", ".docm" } },
        { "وورد", new[] { ".docx", ".docm" } },
        { "docx", new[] { ".docx" } },
        { "excel", new[] { ".xlsx", ".xlsm" } },
        { "اكسل", new[] { ".xlsx", ".xlsm" } },
        { "إكسل", new[] { ".xlsx", ".xlsm" } },
        { "xlsx", new[] { ".xlsx" } },
        { "صورة", new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tiff" } },
        { "صور", new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tiff" } },
        { "png", new[] { ".png" } },
        { "jpg", new[] { ".jpg", ".jpeg" } }
    };

    private static readonly Dictionary<string, string[]> CommonArabicVariants = new(StringComparer.OrdinalIgnoreCase)
    {
        { "فواتير", new[] { "فاتورة" } },
        { "فاتورة", new[] { "فواتير" } },
        { "عقود", new[] { "عقد" } },
        { "عقد", new[] { "عقود" } },
        { "تقارير", new[] { "تقرير" } },
        { "تقرير", new[] { "تقارير" } },
        { "حسابات", new[] { "حساب" } },
        { "مستندات", new[] { "مستند", "وثيقة" } },
        { "مبيعات", new[] { "مبيع", "بيع" } }
    };

    public AgentPlanner(
        IOllamaClient? ollamaClient = null,
        ITextNormalizer? normalizer = null,
        IArabicStemmer? stemmer = null,
        AgentPlannerOptions? options = null)
    {
        _ollamaClient = ollamaClient;
        _normalizer = normalizer ?? new ArabicTextNormalizer();
        _stemmer = stemmer ?? new ArabicLightStemmer();
        _analyzer = new QueryAnalyzer();
        _options = options ?? new AgentPlannerOptions();
    }

    public async Task<SearchPlan> PlanAsync(
        string userQuery,
        IReadOnlyList<string>? scopePaths = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userQuery);

        // Always compute deterministic baseline plan first
        var deterministicPlan = BuildDeterministicPlan(userQuery, scopePaths);

        if (_ollamaClient == null)
        {
            return deterministicPlan;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.TimeoutMs);

            var systemPrompt = @"أنت مساعد ذكي لتخطيط البحث في ملفات الحاسوب.
حلل استفسار المستخدم واستخرج الكلمات الدلالية وأي مرادفات أو لهجات مصرية/عامية، وأي امتدادات ملفات مطلوبة.
أخرج النتيجة بصيغة JSON حصراً بالشكل التالي:
{
  ""keywords"": [""كلمة1"", ""كلمة2""],
  ""variants"": [""مرادف1"", ""مرادف2""],
  ""extensions"": ["".pdf""],
  ""intent"": ""search_files""
}";

            var req = new ChatRequest(
                Model: _options.ModelName,
                Messages: new[]
                {
                    new ChatMessage("system", systemPrompt),
                    new ChatMessage("user", userQuery)
                },
                Temperature: _options.Temperature,
                KeepAlive: "2m"
            );

            var resp = await _ollamaClient.ChatAsync(req, cts.Token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(resp.Content))
            {
                var parsed = ParseLlmPlanJson(resp.Content, deterministicPlan);
                if (parsed != null)
                {
                    return parsed;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout reached: gracefully use deterministic plan
        }
        catch
        {
            // Model failure / non-fatal: fallback to deterministic plan
        }

        return deterministicPlan;
    }

    private SearchPlan BuildDeterministicPlan(string userQuery, IReadOnlyList<string>? scopePaths)
    {
        var norm = _normalizer.Normalize(userQuery, NormalizationProfile.Search);
        var analysis = _analyzer.Analyze(norm.ProcessedText);

        var keywords = analysis.KeyTerms.Count > 0
            ? analysis.KeyTerms
            : norm.ProcessedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kw in keywords)
        {
            var stem = _stemmer.Stem(kw);
            if (!string.IsNullOrWhiteSpace(stem) && !stem.Equals(kw, StringComparison.OrdinalIgnoreCase))
            {
                variants.Add(stem);
            }

            if (CommonArabicVariants.TryGetValue(kw, out var commonVars))
            {
                foreach (var cv in commonVars)
                {
                    variants.Add(cv);
                }
            }
        }

        // Detect requested extensions
        var requestedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in ExtensionMappings)
        {
            if (userQuery.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                norm.ProcessedText.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var ext in kvp.Value)
                {
                    requestedExtensions.Add(ext);
                }
            }
        }

        return new SearchPlan(
            RawQuery: userQuery,
            NormalizedQuery: norm.ProcessedText,
            Keywords: keywords.ToList(),
            DialectVariants: variants.ToList(),
            ScopePaths: scopePaths,
            FileExtensions: requestedExtensions.Count > 0 ? requestedExtensions.ToList() : null,
            Intent: "search_files",
            Limit: 20
        );
    }

    private static SearchPlan? ParseLlmPlanJson(string jsonText, SearchPlan fallback)
    {
        try
        {
            // Clean markdown code fences if model enclosed JSON in ```json ... ```
            var text = jsonText.Trim();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLineEnd = text.IndexOf('\n');
                if (firstLineEnd > 0)
                {
                    text = text[(firstLineEnd + 1)..];
                }
                var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence >= 0)
                {
                    text = text[..lastFence].Trim();
                }
            }

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            var keywords = new List<string>();
            if (root.TryGetProperty("keywords", out var kwProp) && kwProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in kwProp.EnumerateArray())
                {
                    var val = item.GetString();
                    if (!string.IsNullOrWhiteSpace(val)) keywords.Add(val);
                }
            }

            var variants = new List<string>();
            if (root.TryGetProperty("variants", out var varProp) && varProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in varProp.EnumerateArray())
                {
                    var val = item.GetString();
                    if (!string.IsNullOrWhiteSpace(val)) variants.Add(val);
                }
            }

            var extensions = new List<string>();
            if (root.TryGetProperty("extensions", out var extProp) && extProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in extProp.EnumerateArray())
                {
                    var val = item.GetString();
                    if (!string.IsNullOrWhiteSpace(val)) extensions.Add(val.StartsWith('.') ? val : "." + val);
                }
            }

            var intent = "search_files";
            if (root.TryGetProperty("intent", out var intentProp) && intentProp.ValueKind == JsonValueKind.String)
            {
                intent = intentProp.GetString() ?? "search_files";
            }

            return fallback with
            {
                Keywords = keywords.Count > 0 ? keywords : fallback.Keywords,
                DialectVariants = variants.Count > 0 ? variants : fallback.DialectVariants,
                FileExtensions = extensions.Count > 0 ? extensions : fallback.FileExtensions,
                Intent = intent
            };
        }
        catch
        {
            return null;
        }
    }
}
