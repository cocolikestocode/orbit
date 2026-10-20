namespace Orbit.Core.Tests;

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Orbit.Core.Models;
using Orbit.Core.Services;
using Orbit.Core.Services.Impl;
using Orbit.Engines.Execution;
using Xunit;

public class ArchiveAndPdfEngineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileIdentifier _identifier;
    private readonly ArchiveProcessingEngine _archiveEngine;
    private readonly PdfProcessingEngine _pdfEngine;
    private readonly MediaProcessingEngine _mediaEngine;

    public ArchiveAndPdfEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Orbit_Engines_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _identifier = new FileIdentifier();
        _archiveEngine = new ArchiveProcessingEngine();
        _pdfEngine = new PdfProcessingEngine();
        _mediaEngine = new MediaProcessingEngine();
    }

    [Fact]
    public async Task ArchiveEngine_ShouldCreateZipArchive_AndVerifyIntegrity()
    {
        // 1. Create two test files
        var file1 = Path.Combine(_tempDir, "document1.txt");
        var file2 = Path.Combine(_tempDir, "document2.txt");
        await File.WriteAllTextAsync(file1, "First test document for Orbit zip test.");
        await File.WriteAllTextAsync(file2, "Second test document with more payload.");

        var id1 = _identifier.Identify(file1);
        var id2 = _identifier.Identify(file2);

        // 2. Request archive creation
        var request = new OperationRequest
        {
            OperationId = "archive-create-zip",
            InputFiles = new[] { id1, id2 },
            OutputDirectory = _tempDir
        };

        var result = await _archiveEngine.ExecuteAsync(request, new Progress<OperationProgress>());

        // 3. Verify creation
        result.Success.Should().BeTrue();
        result.OutputFiles.Should().NotBeNullOrEmpty();
        var zipPath = result.OutputFiles![0];
        File.Exists(zipPath).Should().BeTrue();
        Path.GetExtension(zipPath).Should().Be(".zip");

        // 4. Test integrity analysis operation on newly created zip
        var zipIdentity = _identifier.Identify(zipPath);
        var analyzeRequest = new OperationRequest
        {
            OperationId = "archive-analyze-integrity",
            InputFiles = new[] { zipIdentity }
        };

        var analyzeResult = await _archiveEngine.ExecuteAsync(analyzeRequest, new Progress<OperationProgress>());
        analyzeResult.Success.Should().BeTrue();
        analyzeResult.ResultMetadata.Should().ContainKey($"{zipIdentity.FileName}_IntegrityPassed");
        analyzeResult.ResultMetadata[$"{zipIdentity.FileName}_IntegrityPassed"].Should().Be(true);
        analyzeResult.ResultMetadata[$"{zipIdentity.FileName}_EntryCount"].Should().Be(2);
    }

    [Fact]
    public async Task ArchiveEngine_ShouldExtractArchiveSafely()
    {
        // 1. Create zip with nested files
        var zipPath = Path.Combine(_tempDir, "extract_sample.zip");
        using (var fs = File.Create(zipPath))
        using (var zip = new System.IO.Compression.ZipArchive(fs, ZipArchiveMode.Create))
        {
            var e1 = zip.CreateEntry("subfolder/fileA.txt");
            using (var w = new StreamWriter(e1.Open())) { w.Write("Content A"); }

            var e2 = zip.CreateEntry("fileB.txt");
            using (var w = new StreamWriter(e2.Open())) { w.Write("Content B"); }
        }

        var extractDir = Path.Combine(_tempDir, "extracted_output");

        // 2. Extract
        var zipIdentity = _identifier.Identify(zipPath);
        var request = new OperationRequest
        {
            OperationId = "archive-extract-here",
            InputFiles = new[] { zipIdentity },
            OutputDirectory = extractDir
        };

        var result = await _archiveEngine.ExecuteAsync(request, new Progress<OperationProgress>());

        // 3. Verify
        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "subfolder", "fileA.txt")).Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "fileB.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ArchiveEngine_ShouldExtractToSmartEnclosingFolder()
    {
        var zipPath = Path.Combine(_tempDir, "bundle_test.zip");
        using (var fs = File.Create(zipPath))
        using (var zip = new System.IO.Compression.ZipArchive(fs, ZipArchiveMode.Create))
        {
            var e1 = zip.CreateEntry("alpha.txt");
            using (var w = new StreamWriter(e1.Open())) { w.Write("Alpha payload"); }
            var e2 = zip.CreateEntry("beta.txt");
            using (var w = new StreamWriter(e2.Open())) { w.Write("Beta payload"); }
        }

        var zipIdentity = _identifier.Identify(zipPath);
        var request = new OperationRequest
        {
            OperationId = "archive-extract-folder",
            InputFiles = new[] { zipIdentity },
            OutputDirectory = _tempDir
        };

        var result = await _archiveEngine.ExecuteAsync(request, new Progress<OperationProgress>());

        result.Success.Should().BeTrue();
        var targetEnclosingFolder = Path.Combine(_tempDir, "bundle_test");
        Directory.Exists(targetEnclosingFolder).Should().BeTrue();
        File.Exists(Path.Combine(targetEnclosingFolder, "alpha.txt")).Should().BeTrue();
        File.Exists(Path.Combine(targetEnclosingFolder, "beta.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ArchiveEngine_ShouldRejectZipSlip_PathTraversal()
    {
        // 1. Craft a malicious zip containing a Zip-Slip path traversal entry
        var maliciousZip = Path.Combine(_tempDir, "zip_slip_attack.zip");
        using (var fs = File.Create(maliciousZip))
        using (var zip = new System.IO.Compression.ZipArchive(fs, ZipArchiveMode.Create))
        {
            var slipEntry = zip.CreateEntry("../../escaped_file.txt");
            using var writer = new StreamWriter(slipEntry.Open());
            writer.Write("MALICIOUS PAYLOAD ESCAPING DESTINATION");
        }

        var safeDestDir = Path.Combine(_tempDir, "safe_target_dir");
        Directory.CreateDirectory(safeDestDir);

        // 2. Direct call to ExtractArchiveSafelyAsync must throw SecurityException
        var act = async () => await ArchiveProcessingEngine.ExtractArchiveSafelyAsync(maliciousZip, safeDestDir);
        await act.Should().ThrowAsync<SecurityException>()
            .WithMessage("*Zip-Slip*");

        // 3. Engine execution must return failure with security message
        var zipIdentity = _identifier.Identify(maliciousZip);
        var request = new OperationRequest
        {
            OperationId = "archive-extract-here",
            InputFiles = new[] { zipIdentity },
            OutputDirectory = safeDestDir
        };

        var result = await _archiveEngine.ExecuteAsync(request, new Progress<OperationProgress>());
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Zip-Slip");

        // Verify the escaped file was NOT created outside
        File.Exists(Path.Combine(_tempDir, "escaped_file.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task ArchiveEngine_ShouldDetectArchiveBomb_WhenByteThresholdExceeded()
    {
        var bombZip = Path.Combine(_tempDir, "bomb_test.zip");
        using (var fs = File.Create(bombZip))
        using (var zip = new System.IO.Compression.ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("large_data.txt");
            using var w = new StreamWriter(entry.Open());
            w.Write(new string('A', 5000));
        }

        var destDir = Path.Combine(_tempDir, "bomb_extract");
        Directory.CreateDirectory(destDir);

        // Call with a strict 100 byte test limit
        var act = async () => await ArchiveProcessingEngine.ExtractArchiveSafelyAsync(
            bombZip,
            destDir,
            default,
            maxTotalBytes: 100);

        await act.Should().ThrowAsync<SecurityException>()
            .WithMessage("*Archive bomb detected*");
    }

    [Fact]
    public async Task PdfEngine_ShouldConvertMarkdownToHtmlAndPdf()
    {
        // 1. Create a markdown file
        var mdPath = Path.Combine(_tempDir, "sample_document.md");
        var markdownText = @"# Orbit Project Architecture

Orbit is a global gesture-driven file operations utility.

## Key Features
- **Zero-Latency**: Low-level Win32 hooks
- *Native Processing*: SharpCompress and pure managed PDF
- Code Blocks:

```csharp
public class OrbitEngine {
    public void Execute() => Console.WriteLine(""Orbit"");
}
```

> Built for Windows 10 and 11.";

        await File.WriteAllTextAsync(mdPath, markdownText);
        var identity = _identifier.Identify(mdPath);

        // 2. Execute doc-markdown-to-html-pdf
        var request = new OperationRequest
        {
            OperationId = "doc-markdown-to-html-pdf",
            InputFiles = new[] { identity },
            OutputDirectory = _tempDir
        };

        var result = await _pdfEngine.ExecuteAsync(request, new Progress<OperationProgress>());

        // 3. Verify outputs
        result.Success.Should().BeTrue();
        result.OutputFiles.Should().NotBeNull();
        result.OutputFiles!.Length.Should().Be(2);

        var htmlFile = result.OutputFiles.FirstOrDefault(f => f.EndsWith(".html"));
        var pdfFile = result.OutputFiles.FirstOrDefault(f => f.EndsWith(".pdf"));

        htmlFile.Should().NotBeNull();
        pdfFile.Should().NotBeNull();

        File.Exists(htmlFile!).Should().BeTrue();
        File.Exists(pdfFile!).Should().BeTrue();

        var htmlContent = await File.ReadAllTextAsync(htmlFile!);
        htmlContent.Should().Contain("<h1>Orbit Project Architecture</h1>");
        htmlContent.Should().Contain("<strong>Zero-Latency</strong>");
        htmlContent.Should().Contain("<code>");

        var pdfBytes = await File.ReadAllBytesAsync(pdfFile!);
        var pdfHeader = Encoding.Latin1.GetString(pdfBytes, 0, 10);
        pdfHeader.Should().StartWith("%PDF-1.4");
    }

    [Fact]
    public async Task PdfEngine_ShouldExtractText_AndAnalyzePdfInfo()
    {
        // 1. Create a clean PDF file using WriteSimplePdfDocumentAsync
        var pdfPath = Path.Combine(_tempDir, "analysis_sample.pdf");
        var pageText = "Orbit System Diagnostics\nPage 1 Content with critical payload.\nTested and verified.";
        await PdfProcessingEngine.WriteSimplePdfDocumentAsync(
            pdfPath,
            new System.Collections.Generic.List<string> { pageText },
            "Orbit System Diagnostics");

        var identity = _identifier.Identify(pdfPath);

        // 2. Test pdf-analyze-info
        var analyzeRequest = new OperationRequest
        {
            OperationId = "pdf-analyze-info",
            InputFiles = new[] { identity }
        };

        var analyzeResult = await _pdfEngine.ExecuteAsync(analyzeRequest, new Progress<OperationProgress>());
        analyzeResult.Success.Should().BeTrue();
        analyzeResult.ResultMetadata.Should().ContainKey($"{identity.FileName}_PdfVersion");
        analyzeResult.ResultMetadata[$"{identity.FileName}_PdfVersion"].Should().Be("1.4");
        analyzeResult.ResultMetadata[$"{identity.FileName}_HasCatalog"].Should().Be(true);
        analyzeResult.ResultMetadata[$"{identity.FileName}_PageCount"].Should().Be(1);

        // 3. Test pdf-extract-text
        var extractRequest = new OperationRequest
        {
            OperationId = "pdf-extract-text",
            InputFiles = new[] { identity }
        };

        var extractResult = await _pdfEngine.ExecuteAsync(extractRequest, new Progress<OperationProgress>());
        extractResult.Success.Should().BeTrue();
        extractResult.OutputFiles.Should().NotBeNullOrEmpty();

        var textPath = extractResult.OutputFiles![0];
        File.Exists(textPath).Should().BeTrue();
        var extractedText = await File.ReadAllTextAsync(textPath);
        extractedText.Should().Contain("Orbit System Diagnostics");
        extractedText.Should().Contain("Page 1 Content");
    }

    [Fact]
    public async Task PdfEngine_ShouldMergeMultiplePdfs()
    {
        // 1. Create two PDF files
        var pdf1 = Path.Combine(_tempDir, "doc1.pdf");
        var pdf2 = Path.Combine(_tempDir, "doc2.pdf");

        await PdfProcessingEngine.WriteSimplePdfDocumentAsync(
            pdf1,
            new System.Collections.Generic.List<string> { "Section One Contents: Orbit Core Engine" },
            "Document 1 Title");

        await PdfProcessingEngine.WriteSimplePdfDocumentAsync(
            pdf2,
            new System.Collections.Generic.List<string> { "Section Two Contents: Orbit Engines & Gestures" },
            "Document 2 Title");

        var id1 = _identifier.Identify(pdf1);
        var id2 = _identifier.Identify(pdf2);

        // 2. Execute pdf-merge
        var request = new OperationRequest
        {
            OperationId = "pdf-merge",
            InputFiles = new[] { id1, id2 },
            OutputDirectory = _tempDir
        };

        var result = await _pdfEngine.ExecuteAsync(request, new Progress<OperationProgress>());

        // 3. Verify
        result.Success.Should().BeTrue();
        result.OutputFiles.Should().NotBeNullOrEmpty();
        var mergedFile = result.OutputFiles![0];
        File.Exists(mergedFile).Should().BeTrue();

        var (reportPath, metadata) = await PdfProcessingEngine.AnalyzePdfInfoAsync(mergedFile);
        metadata["PdfVersion"].Should().Be("1.4");
        metadata["PageCount"].Should().Be(2);

        var extractedTextPath = await PdfProcessingEngine.ExtractTextFromPdfAsync(mergedFile);
        var mergedText = await File.ReadAllTextAsync(extractedTextPath);
        mergedText.Should().Contain("Section One Contents");
        mergedText.Should().Contain("Section Two Contents");
    }

    [Fact]
    public async Task MediaEngine_ShouldHandleMissingFfmpegGracefully_OrAnalyzeAvailableFile()
    {
        // 1. Create a dummy test audio file (WAV RIFF header)
        var wavPath = Path.Combine(_tempDir, "test_audio.wav");
        using (var fs = File.Create(wavPath))
        {
            var header = Encoding.ASCII.GetBytes("RIFF....WAVEfmt ....data....");
            await fs.WriteAsync(header);
            var dummyData = new byte[1024];
            await fs.WriteAsync(dummyData);
        }

        var identity = _identifier.Identify(wavPath);

        // 2. Test media-analyze-info
        var analyzeRequest = new OperationRequest
        {
            OperationId = "media-analyze-info",
            InputFiles = new[] { identity },
            OutputDirectory = _tempDir
        };

        var analyzeResult = await _mediaEngine.ExecuteAsync(analyzeRequest, new Progress<OperationProgress>());
        analyzeResult.Success.Should().BeTrue();
        analyzeResult.OutputFiles.Should().NotBeNullOrEmpty();
        File.Exists(analyzeResult.OutputFiles![0]).Should().BeTrue();

        // 3. If ffmpeg is not available, video transcoding should fail gracefully with actionable instruction
        var ffmpegExists = !string.IsNullOrEmpty(MediaProcessingEngine.FindFfmpegExecutable());
        if (!ffmpegExists)
        {
            var transcodeRequest = new OperationRequest
            {
                OperationId = "video-convert-mp4",
                InputFiles = new[] { identity },
                OutputDirectory = _tempDir
            };

            var transcodeResult = await _mediaEngine.ExecuteAsync(transcodeRequest, new Progress<OperationProgress>());
            transcodeResult.Success.Should().BeFalse();
            transcodeResult.ErrorMessage.Should().Contain("FFmpeg was not detected");
            transcodeResult.ErrorMessage.Should().Contain("winget install Gyan.FFmpeg");
        }
    }

    [Fact]
    public async Task JobManager_ShouldDispatchToArchiveAndPdfEngines()
    {
        var allEngines = new IProcessingEngine[]
        {
            new ImageProcessingEngine(),
            new TextDataProcessingEngine(),
            _archiveEngine,
            _pdfEngine,
            _mediaEngine
        };

        var jobManager = new JobManager(allEngines);

        // 1. Dispatch PDF info analysis
        var pdfPath = Path.Combine(_tempDir, "job_test_sample.pdf");
        await PdfProcessingEngine.WriteSimplePdfDocumentAsync(
            pdfPath,
            new System.Collections.Generic.List<string> { "Job Manager Multi-Engine Test Content" },
            "Job Manager Test");

        var identity = _identifier.Identify(pdfPath);
        var request = new OperationRequest
        {
            OperationId = "pdf-analyze-info",
            InputFiles = new[] { identity }
        };

        var tcs = new TaskCompletionSource<JobInfo>();
        jobManager.JobCompleted += (sender, job) =>
        {
            if (job.OperationId == "pdf-analyze-info")
            {
                tcs.TrySetResult(job);
            }
        };

        var jobId = await jobManager.EnqueueAsync(request);
        jobId.Should().NotBeNullOrEmpty();

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(4000));
        completed.Should().Be(tcs.Task);

        var finished = await tcs.Task;
        finished.Result.Should().NotBeNull();
        finished.Result!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ArchiveEngine_ShouldRejectNestedZipSlip_PathTraversal()
    {
        var maliciousZip = Path.Combine(_tempDir, "nested_zip_slip.zip");
        using (var fs = File.Create(maliciousZip))
        using (var zip = new System.IO.Compression.ZipArchive(fs, ZipArchiveMode.Create))
        {
            var slipEntry = zip.CreateEntry("some/sub/dir/../../../../escape_nested.txt");
            using var writer = new StreamWriter(slipEntry.Open());
            writer.Write("MALICIOUS NESTED ESCAPE");
        }

        var safeDestDir = Path.Combine(_tempDir, "safe_nested_target");
        Directory.CreateDirectory(safeDestDir);

        var act = async () => await ArchiveProcessingEngine.ExtractArchiveSafelyAsync(maliciousZip, safeDestDir);
        await act.Should().ThrowAsync<SecurityException>()
            .WithMessage("*Zip-Slip*");
    }

    [Fact]
    public async Task PdfEngine_ShouldExtractHexStringsAndTjArrays()
    {
        // 1. Manually craft a raw PDF stream with TJ array and hex string
        var pdfPath = Path.Combine(_tempDir, "raw_stream_test.pdf");
        var contentStream = @"BT
/F1 12 Tf
[(Orbit) 20 ( ) 20 (Stream) 20 ( ) 20 (Parser)] TJ
<53756363657373> Tj
ET";
        var streamBytes = Encoding.Latin1.GetBytes(contentStream);

        var pdfContent = new StringBuilder();
        pdfContent.AppendLine("%PDF-1.4");
        pdfContent.AppendLine("1 0 obj");
        pdfContent.AppendLine("<< /Type /Catalog /Pages 2 0 R >>");
        pdfContent.AppendLine("endobj");
        pdfContent.AppendLine("2 0 obj");
        pdfContent.AppendLine("<< /Type /Pages /Kids [ 3 0 R ] /Count 1 >>");
        pdfContent.AppendLine("endobj");
        pdfContent.AppendLine("3 0 obj");
        pdfContent.AppendLine("<< /Type /Page /Parent 2 0 R /MediaBox [ 0 0 612 792 ] /Contents 4 0 R >>");
        pdfContent.AppendLine("endobj");
        pdfContent.AppendLine("4 0 obj");
        pdfContent.AppendLine($"<< /Length {streamBytes.Length} >>");
        pdfContent.AppendLine("stream");
        pdfContent.Append(contentStream);
        pdfContent.AppendLine();
        pdfContent.AppendLine("endstream");
        pdfContent.AppendLine("endobj");
        pdfContent.AppendLine("xref");
        pdfContent.AppendLine("0 5");
        pdfContent.AppendLine("0000000000 65535 f ");
        pdfContent.AppendLine("0000000010 00000 n ");
        pdfContent.AppendLine("0000000060 00000 n ");
        pdfContent.AppendLine("0000000120 00000 n ");
        pdfContent.AppendLine("0000000210 00000 n ");
        pdfContent.AppendLine("trailer");
        pdfContent.AppendLine("<< /Size 5 /Root 1 0 R >>");
        pdfContent.AppendLine("startxref");
        pdfContent.AppendLine("350");
        pdfContent.AppendLine("%%EOF");

        await File.WriteAllTextAsync(pdfPath, pdfContent.ToString(), Encoding.Latin1);

        // 2. Extract text
        var extractedPath = await PdfProcessingEngine.ExtractTextFromPdfAsync(pdfPath);
        File.Exists(extractedPath).Should().BeTrue();

        var extractedText = await File.ReadAllTextAsync(extractedPath);
        extractedText.Should().Contain("Orbit Stream Parser");
        extractedText.Should().Contain("Success"); // Hex <53756363657373> = "Success"
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }
}
