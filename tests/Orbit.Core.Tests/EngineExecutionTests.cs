namespace Orbit.Core.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Orbit.Core.Models;
using Orbit.Core.Services;
using Orbit.Core.Services.Impl;
using Orbit.Engines.Execution;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

public class EngineExecutionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileIdentifier _identifier;
    private readonly ImageProcessingEngine _imageEngine;
    private readonly TextDataProcessingEngine _textEngine;
    private readonly JobManager _jobManager;

    public EngineExecutionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Orbit_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _identifier = new FileIdentifier();
        _imageEngine = new ImageProcessingEngine();
        _textEngine = new TextDataProcessingEngine();
        _jobManager = new JobManager(new IProcessingEngine[] { _imageEngine, _textEngine });
    }

    [Fact]
    public async Task ImageProcessingEngine_ShouldConvertPngToWebp()
    {
        // 1. Create a 64x64 test PNG image
        var pngPath = Path.Combine(_tempDir, "test_input.png");
        using (var img = new Image<Rgba32>(64, 64))
        {
            await img.SaveAsPngAsync(pngPath);
        }

        var identity = _identifier.Identify(pngPath);

        // 2. Execute convert operation
        var request = new OperationRequest
        {
            OperationId = "img-convert-webp",
            InputFiles = new[] { identity },
            OutputFormat = ".webp"
        };

        var result = await _imageEngine.ExecuteAsync(request, new Progress<OperationProgress>());

        // 3. Verify
        result.Success.Should().BeTrue();
        result.OutputFiles.Should().NotBeNullOrEmpty();
        var outPath = result.OutputFiles![0];
        File.Exists(outPath).Should().BeTrue();
        Path.GetExtension(outPath).Should().Be(".webp");
    }

    [Fact]
    public async Task TextDataProcessingEngine_ShouldGenerateSha256Checksum()
    {
        // 1. Create a test text file
        var txtPath = Path.Combine(_tempDir, "secret.txt");
        await File.WriteAllTextAsync(txtPath, "Orbit Global File Operations Utility for Windows");

        var identity = _identifier.Identify(txtPath);

        // 2. Execute hash operation
        var request = new OperationRequest
        {
            OperationId = "text-secure-hash",
            InputFiles = new[] { identity }
        };

        var result = await _textEngine.ExecuteAsync(request, new Progress<OperationProgress>());

        // 3. Verify
        result.Success.Should().BeTrue();
        result.OutputFiles.Should().NotBeNullOrEmpty();
        var outPath = result.OutputFiles![0];
        File.Exists(outPath).Should().BeTrue();
        Path.GetExtension(outPath).Should().Be(".sha256");

        var content = await File.ReadAllTextAsync(outPath);
        content.Should().Contain("secret.txt");
    }

    [Fact]
    public async Task JobManager_ShouldEnqueueAndCompleteJob()
    {
        var txtPath = Path.Combine(_tempDir, "job_test.txt");
        await File.WriteAllTextAsync(txtPath, "Line B\nLine A\nLine C\n");

        var identity = _identifier.Identify(txtPath);

        var request = new OperationRequest
        {
            OperationId = "text-edit-sort-lines",
            InputFiles = new[] { identity }
        };

        var tcs = new TaskCompletionSource<JobInfo>();
        _jobManager.JobCompleted += (sender, job) =>
        {
            if (job.OperationId == "text-edit-sort-lines")
            {
                tcs.TrySetResult(job);
            }
        };

        var jobId = await _jobManager.EnqueueAsync(request);
        jobId.Should().NotBeNullOrEmpty();

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        completed.Should().Be(tcs.Task);

        var finishedJob = await tcs.Task;
        finishedJob.Result.Should().NotBeNull();
        finishedJob.Result!.Success.Should().BeTrue();
        finishedJob.Result.OutputFiles.Should().NotBeNullOrEmpty();

        var sortedPath = finishedJob.Result.OutputFiles![0];
        File.Exists(sortedPath).Should().BeTrue();
        var sortedLines = await File.ReadAllLinesAsync(sortedPath);
        sortedLines.Should().ContainInOrder("Line A", "Line B", "Line C");
    }

    [Fact]
    public async Task ImageOperations_WithStandardRegistryIds_ShouldProduceCorrectImageFormat()
    {
        var pngPath = Path.Combine(_tempDir, "sample.png");
        using (var img = new Image<Rgba32>(100, 100))
        {
            await img.SaveAsPngAsync(pngPath);
        }

        var identity = _identifier.Identify(pngPath);

        // Test 1: image-png-to-jpg
        var reqJpg = new OperationRequest
        {
            OperationId = "image-png-to-jpg",
            InputFiles = new[] { identity }
        };
        var resJpg = await _imageEngine.ExecuteAsync(reqJpg, new Progress<OperationProgress>());
        resJpg.Success.Should().BeTrue();
        Path.GetExtension(resJpg.OutputFiles![0]).Should().Be(".jpg");

        // Test 2: image-png-to-webp
        var reqWebp = new OperationRequest
        {
            OperationId = "image-png-to-webp",
            InputFiles = new[] { identity }
        };
        var resWebp = await _imageEngine.ExecuteAsync(reqWebp, new Progress<OperationProgress>());
        resWebp.Success.Should().BeTrue();
        Path.GetExtension(resWebp.OutputFiles![0]).Should().Be(".webp");

        // Test 3: image-rotate
        var reqRot = new OperationRequest
        {
            OperationId = "image-rotate",
            InputFiles = new[] { identity }
        };
        var resRot = await _imageEngine.ExecuteAsync(reqRot, new Progress<OperationProgress>());
        resRot.Success.Should().BeTrue();
        Path.GetFileName(resRot.OutputFiles![0]).Should().Contain("rotated");
    }

    [Fact]
    public async Task JobManager_RoutingImageOp_ShouldNeverProduceTextFile()
    {
        var pngPath = Path.Combine(_tempDir, "routing_test.png");
        using (var img = new Image<Rgba32>(32, 32))
        {
            await img.SaveAsPngAsync(pngPath);
        }

        var identity = _identifier.Identify(pngPath);

        var request = new OperationRequest
        {
            OperationId = "image-png-to-jpg",
            InputFiles = new[] { identity }
        };

        var tcs = new TaskCompletionSource<JobInfo>();
        _jobManager.JobCompleted += (sender, job) =>
        {
            if (job.OperationId == "image-png-to-jpg")
            {
                tcs.TrySetResult(job);
            }
        };

        await _jobManager.EnqueueAsync(request);
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(4000));
        completed.Should().Be(tcs.Task);

        var finished = await tcs.Task;
        finished.Result!.Success.Should().BeTrue();
        var outPath = finished.Result.OutputFiles![0];

        // MUST be a real JPEG image, NEVER an info.txt!
        Path.GetExtension(outPath).Should().Be(".jpg");
        Path.GetExtension(outPath).Should().NotBe(".txt");
        File.Exists(outPath).Should().BeTrue();
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
