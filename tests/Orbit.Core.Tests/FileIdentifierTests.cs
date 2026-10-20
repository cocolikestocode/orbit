namespace Orbit.Core.Tests;

using System;
using System.IO;
using FluentAssertions;
using Orbit.Core.Models;
using Orbit.Core.Services.Impl;
using Xunit;

public class FileIdentifierTests
{
    private readonly FileIdentifier _identifier = new();

    [Theory]
    // 25 distinct extension tests covering all major families
    [InlineData(".png", FileFamily.Image)]
    [InlineData(".jpg", FileFamily.Image)]
    [InlineData(".webp", FileFamily.Image)]
    [InlineData(".gif", FileFamily.Image)]
    [InlineData(".pdf", FileFamily.Document)]
    [InlineData(".docx", FileFamily.Document)]
    [InlineData(".mp4", FileFamily.Video)]
    [InlineData(".mkv", FileFamily.Video)]
    [InlineData(".mp3", FileFamily.Audio)]
    [InlineData(".flac", FileFamily.Audio)]
    [InlineData(".zip", FileFamily.Archive)]
    [InlineData(".7z", FileFamily.Archive)]
    [InlineData(".tar", FileFamily.Archive)]
    [InlineData(".xlsx", FileFamily.Spreadsheet)]
    [InlineData(".csv", FileFamily.Spreadsheet)]
    [InlineData(".pptx", FileFamily.Presentation)]
    [InlineData(".cs", FileFamily.Code)]
    [InlineData(".py", FileFamily.Code)]
    [InlineData(".txt", FileFamily.Text)]
    [InlineData(".md", FileFamily.Text)]
    [InlineData(".json", FileFamily.Data)]
    [InlineData(".xml", FileFamily.Data)]
    [InlineData(".ttf", FileFamily.Font)]
    [InlineData(".blend", FileFamily.Design)]
    [InlineData(".obj", FileFamily.ThreeDimensional)]
    [InlineData(".epub", FileFamily.Ebook)]
    [InlineData(".eml", FileFamily.Email)]
    [InlineData(".cer", FileFamily.Certificate)]
    [InlineData(".exe", FileFamily.Executable)]
    [InlineData(".iso", FileFamily.DiskImage)]
    [InlineData(".sqlite", FileFamily.Database)]
    [InlineData(".log", FileFamily.Log)]
    public void DetectFamily_ShouldReturnExpectedFamily_ForVariousExtensions(string extension, FileFamily expectedFamily)
    {
        var result = _identifier.DetectFamily(extension);
        result.Should().Be(expectedFamily);
    }

    [Theory]
    [InlineData("PNG", FileFamily.Image)]
    [InlineData("PDF", FileFamily.Document)]
    [InlineData(".ZIP", FileFamily.Archive)]
    [InlineData("mp4", FileFamily.Video)]
    public void DetectFamily_ShouldBeCaseInsensitive_AndSupportMissingDot(string extension, FileFamily expectedFamily)
    {
        var result = _identifier.DetectFamily(extension);
        result.Should().Be(expectedFamily);
    }

    [Fact]
    public void DetectFamily_ShouldReturnUnknown_ForUnknownExtension()
    {
        var result = _identifier.DetectFamily(".completely_unknown_xyz123");
        result.Should().Be(FileFamily.Unknown);
    }

    [Theory]
    [InlineData("sample.png", "image/png")]
    [InlineData("sample.jpg", "image/jpeg")]
    [InlineData("sample.pdf", "application/pdf")]
    [InlineData("sample.json", "application/json")]
    [InlineData("sample.zip", "application/zip")]
    [InlineData("sample.mp4", "video/mp4")]
    [InlineData("sample.mp3", "audio/mpeg")]
    public void DetectMimeType_ShouldReturnExpectedMimeType(string fileName, string expectedMime)
    {
        var result = _identifier.DetectMimeType(fileName);
        result.Should().Be(expectedMime);
    }

    [Fact]
    public void Identify_ShouldDetectFolder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OrbitTestFolder_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var identity = _identifier.Identify(tempDir);

            identity.Family.Should().Be(FileFamily.Folder);
            identity.MimeType.Should().Be("inode/directory");
            identity.IsReadable.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Identify_ShouldDetectMagicBytes_ForPngFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "magic_test_" + Guid.NewGuid().ToString("N") + ".tmp");
        var pngSignature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
        File.WriteAllBytes(tempFile, pngSignature);

        try
        {
            var identity = _identifier.Identify(tempFile);

            identity.Family.Should().Be(FileFamily.Image);
            identity.MimeType.Should().Be("image/png");
            identity.Metadata.Should().ContainKey("MagicFormat").WhoseValue.Should().Be("PNG");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Identify_ShouldDetectMagicBytes_ForPdfFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "magic_test_" + Guid.NewGuid().ToString("N") + ".tmp");
        var pdfSignature = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 };
        File.WriteAllBytes(tempFile, pdfSignature);

        try
        {
            var identity = _identifier.Identify(tempFile);

            identity.Family.Should().Be(FileFamily.Document);
            identity.MimeType.Should().Be("application/pdf");
            identity.Metadata.Should().ContainKey("MagicFormat").WhoseValue.Should().Be("PDF");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void IdentifyMultiple_ShouldReturnIdentitiesForAllFiles()
    {
        var paths = new[] { "file1.png", "file2.pdf", "file3.mp3" };
        var results = _identifier.IdentifyMultiple(paths);

        results.Should().HaveCount(3);
        results[0].Family.Should().Be(FileFamily.Image);
        results[1].Family.Should().Be(FileFamily.Document);
        results[2].Family.Should().Be(FileFamily.Audio);
    }
}
