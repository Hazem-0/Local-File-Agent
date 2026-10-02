using System.IO;
using FluentAssertions;
using LocalFileAgent.Application.FileSystem;
using LocalFileAgent.Application.Search;
using LocalFileAgent.Domain.Search;
using LocalFileAgent.Text;
using Xunit;

namespace LocalFileAgent.Application.Tests;

public class SearchResultViewModelAndLauncherTests
{
    private sealed class MockFileLauncher : IFileLauncher
    {
        public string? LastOpenedFile { get; private set; }
        public string? LastOpenedFolder { get; private set; }

        public bool OpenFile(string filePath)
        {
            LastOpenedFile = filePath;
            return true;
        }

        public bool OpenContainingFolder(string filePath)
        {
            LastOpenedFolder = filePath;
            return true;
        }
    }

    [Fact]
    public void SearchResultViewModel_SetsAppropriateIconsAndBadges()
    {
        var normalizer = new ArabicTextNormalizer();
        var launcher = new MockFileLauncher();

        var pdfItem = new SearchResultItem("D:\\docs\\report.pdf", "report.pdf", 1, "text_layer", 0.95f, "نص التقرير");
        var pdfVm = new SearchResultViewModel(pdfItem, "تقرير", normalizer, launcher);

        pdfVm.Extension.Should().Be("PDF");
        pdfVm.FileTypeIcon.Should().Be("📕");
        pdfVm.FileTypeBadgeBg.Should().Be("#FEE2E2");

        var pngItem = new SearchResultItem("D:\\images\\scan.png", "scan.png", 2, "ocr_win", 0.88f, "نص الصورة");
        var pngVm = new SearchResultViewModel(pngItem, "صورة", normalizer, launcher);

        pngVm.Extension.Should().Be("PNG");
        pngVm.FileTypeIcon.Should().Be("🖼️");
        pngVm.SourceBadgeBg.Should().Be("#FEF3C7");
    }

    [Fact]
    public void SearchResultViewModel_CleansMarkdownAsterisksFromSnippets()
    {
        var normalizer = new ArabicTextNormalizer();
        var rawSnippet = "**وصف محتوى الصورة:**\n\nتظهر الصورة فاتورة أو إيصال.\n\n**نوع المستند:** فاتورة مالية.";

        var item = new SearchResultItem("D:\\docs\\invoice.png", "invoice.png", 1, "vlm", 0.90f, rawSnippet);
        var vm = new SearchResultViewModel(item, "فاتورة", normalizer);

        vm.RawSnippet.Should().NotContain("**");
        vm.RawSnippet.Should().Contain("وصف المحتوى:");
        vm.RawSnippet.Should().Contain("نوع المستند:");
    }

    [Fact]
    public void SearchResultViewModel_OpenFileAndFolderCommands_InvokeLauncher()
    {
        var normalizer = new ArabicTextNormalizer();
        var launcher = new MockFileLauncher();

        var item = new SearchResultItem("D:\\docs\\invoice.pdf", "invoice.pdf", 1, "text_layer", 0.95f, "محتوى الفاتورة");
        var vm = new SearchResultViewModel(item, "فاتورة", normalizer, launcher);

        vm.OpenFileCommand.Execute(null);
        launcher.LastOpenedFile.Should().Be("D:\\docs\\invoice.pdf");

        vm.OpenContainingFolderCommand.Execute(null);
        launcher.LastOpenedFolder.Should().Be("D:\\docs\\invoice.pdf");
    }

    [Fact]
    public void SafeFileLauncher_ReturnsFalseForInvalidOrNonExistentFiles()
    {
        var launcher = SafeFileLauncher.Instance;

        launcher.OpenFile(string.Empty).Should().BeFalse();
        launcher.OpenFile("   ").Should().BeFalse();
        launcher.OpenFile("C:\\invalid\\non_existent_path_xyz_1234.pdf").Should().BeFalse();

        launcher.OpenContainingFolder(string.Empty).Should().BeFalse();
        launcher.OpenContainingFolder("   ").Should().BeFalse();
    }
}
