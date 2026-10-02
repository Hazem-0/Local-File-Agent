using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LocalFileAgent.Application.Agent;
using LocalFileAgent.Application.FileSystem;
using LocalFileAgent.Application.Indexing;
using LocalFileAgent.Application.Search;
using LocalFileAgent.Application.Throttling;
using LocalFileAgent.Domain.Agent;
using LocalFileAgent.Domain.FileSystem;
using LocalFileAgent.Domain.Throttling;
using LocalFileAgent.Infrastructure.Throttling;
using LocalFileAgent.Domain.Models;
using LocalFileAgent.Domain.Search;
using LocalFileAgent.Domain.Storage;
using LocalFileAgent.Domain.Text;
using LocalFileAgent.Domain.Worker;
using LocalFileAgent.Infrastructure.Ollama;
using LocalFileAgent.Infrastructure.Storage;
using LocalFileAgent.Infrastructure.Vision;
using LocalFileAgent.Infrastructure.Worker;
using LocalFileAgent.Text;

namespace LocalFileAgent.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureServices((_, services) =>
        {
            // Arabic text processing services
            services.AddSingleton<ITextNormalizer, ArabicTextNormalizer>();
            services.AddSingleton<IChunker, ArabicChunker>();
            services.AddSingleton<IEncodingDetector, EncodingDetector>();
            services.AddSingleton<IArabicOrderFixer, ArabicOrderFixer>();
            services.AddSingleton<ITextQualityGate, TextQualityGate>();

            // File system scanner
            services.AddSingleton<IFileScanner, FileScanner>();

            // Storage, background worker, vector index, and embeddings
            var dbPath = AppDataPaths.GetDatabasePath();
            services.AddSingleton<IIndexStore>(_ => new SqliteIndexStore(dbPath));
            services.AddSingleton<IVectorIndex>(_ => new SqliteVectorIndex(dbPath));
            services.AddSingleton<IVisualVectorIndex>(_ => new SqliteVisualVectorIndex(dbPath));
            services.AddSingleton<IOllamaClient>(_ => new OllamaClient(new System.Net.Http.HttpClient { BaseAddress = new Uri("http://127.0.0.1:11434") }));
            services.AddSingleton<IEmbeddingService, EmbeddingService>();
            services.AddSingleton<IVisualEmbeddingService, VisualEmbeddingService>();
            services.AddSingleton<IImageHasher, ImageHasher>();
            services.AddSingleton<ITier2OcrService, Tier2OcrService>();
            services.AddSingleton<IVisualDescriber, VisualDescriber>();
            // Power status, battery guard, and resource throttling
            services.AddSingleton<IPowerStatusProvider, WindowsPowerStatusProvider>();
            services.AddSingleton<IResourceGovernor, ResourceGovernor>();

            services.AddSingleton<IWorkerClient, WorkerClient>();
            services.AddSingleton<IIndexOrchestrator, IndexOrchestrator>();
            services.AddSingleton<ISearchService, HybridSearchService>();

            // Agent, planning, grounding, and incremental watcher
            services.AddSingleton<IAgentPlanner, AgentPlanner>();
            services.AddSingleton<IGroundingValidator, GroundingValidator>();
            services.AddSingleton<IAgentService, AgentService>();
            services.AddSingleton<IFileWatcherService>(_ => new IncrementalIndexWatcher());

            // UI and ViewModel
            services.AddSingleton<IDispatcherService, WpfDispatcherService>();
            services.AddSingleton<SearchViewModel>();
            services.AddSingleton<MainWindow>();
        });

        _host = builder.Build();
        _host.Start();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        var viewModel = _host.Services.GetRequiredService<SearchViewModel>();
        mainWindow.DataContext = viewModel;
        mainWindow.Show();

        _ = viewModel.RefreshStatsCommand.ExecuteAsync(null);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync().ConfigureAwait(false);
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
