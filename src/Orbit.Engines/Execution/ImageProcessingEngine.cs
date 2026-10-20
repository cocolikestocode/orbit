namespace Orbit.Engines.Execution;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core.Models;
using Orbit.Core.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

public class ImageProcessingEngine : IProcessingEngine
{
    public string EngineId => "image";
    public string DisplayName => "Orbit Native Image Engine";
    public bool IsAvailable => true;

    public Task<EngineCapabilities> GetCapabilitiesAsync()
    {
        return Task.FromResult(new EngineCapabilities
        {
            EngineId = EngineId,
            SupportedInputFormats = new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" },
            SupportedOutputFormats = new[] { ".png", ".jpg", ".webp", ".bmp" },
            SupportedCategories = new[]
            {
                OperationCategory.Convert,
                OperationCategory.Edit,
                OperationCategory.Optimize,
                OperationCategory.Analyze
            },
            SupportsBatch = true
        });
    }

    public Task<bool> ValidateAsync(OperationRequest request)
    {
        if (request.InputFiles.Length == 0) return Task.FromResult(false);
        return Task.FromResult(true);
    }

    public async Task<OperationResult> ExecuteAsync(
        OperationRequest request,
        IProgress<OperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var outputFiles = new System.Collections.Generic.List<string>();

        try
        {
            int total = request.InputFiles.Length;

            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = request.InputFiles[i];
                progress.Report(new OperationProgress
                {
                    OperationId = request.OperationId,
                    PercentComplete = (double)i / total * 100.0,
                    CurrentFile = file.FileName,
                    FilesProcessed = i,
                    TotalFiles = total,
                    StatusMessage = $"Processing {file.FileName}..."
                });

                var outPath = await ProcessSingleImageAsync(file, request.OperationId, request.OutputFormat, cancellationToken);
                if (!string.IsNullOrEmpty(outPath))
                {
                    outputFiles.Add(outPath);
                }
            }

            stopwatch.Stop();
            return new OperationResult
            {
                OperationId = request.OperationId,
                Success = true,
                OutputFiles = outputFiles.ToArray(),
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new OperationResult
            {
                OperationId = request.OperationId,
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    private async Task<string> ProcessSingleImageAsync(
        FileIdentity file,
        string operationId,
        string? explicitOutputFormat,
        CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(file.FilePath) ?? Environment.CurrentDirectory;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(file.FilePath);

        using var image = await Image.LoadAsync(file.FilePath, cancellationToken);

        string targetExt;
        SixLabors.ImageSharp.Formats.IImageEncoder encoder;

        switch (operationId)
        {
            case "img-convert-jpg":
            case "img-png-to-jpg":
            case "img-webp-to-jpg":
                targetExt = ".jpg";
                encoder = new JpegEncoder { Quality = 90 };
                break;

            case "img-convert-webp":
            case "img-png-to-webp":
            case "img-jpg-to-webp":
                targetExt = ".webp";
                encoder = new WebpEncoder { Quality = 85 };
                break;

            case "img-convert-png":
            case "img-jpg-to-png":
            case "img-webp-to-png":
                targetExt = ".png";
                encoder = new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression };
                break;

            case "img-convert-bmp":
                targetExt = ".bmp";
                encoder = new BmpEncoder();
                break;

            case "img-optimize-compress":
            case "img-compress-lossy":
                targetExt = Path.GetExtension(file.FilePath).ToLowerInvariant();
                encoder = targetExt switch
                {
                    ".png" => new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression },
                    ".webp" => new WebpEncoder { Quality = 75 },
                    _ => new JpegEncoder { Quality = 75 }
                };
                nameWithoutExt += "_optimized";
                break;

            case "img-edit-rotate-90":
                image.Mutate(x => x.Rotate(90));
                targetExt = Path.GetExtension(file.FilePath);
                encoder = GetEncoderForExtension(targetExt);
                nameWithoutExt += "_rotated";
                break;

            case "img-edit-flip-h":
                image.Mutate(x => x.Flip(FlipMode.Horizontal));
                targetExt = Path.GetExtension(file.FilePath);
                encoder = GetEncoderForExtension(targetExt);
                nameWithoutExt += "_flipped";
                break;

            default:
                if (!string.IsNullOrEmpty(explicitOutputFormat))
                {
                    targetExt = explicitOutputFormat.StartsWith(".") ? explicitOutputFormat : "." + explicitOutputFormat;
                    encoder = GetEncoderForExtension(targetExt);
                }
                else
                {
                    targetExt = ".webp";
                    encoder = new WebpEncoder { Quality = 85 };
                }
                break;
        }

        var destination = Path.Combine(dir, $"{nameWithoutExt}{targetExt}");
        int counter = 1;
        while (File.Exists(destination))
        {
            destination = Path.Combine(dir, $"{nameWithoutExt}_{counter}{targetExt}");
            counter++;
        }

        await image.SaveAsync(destination, encoder, cancellationToken);
        return destination;
    }

    private static SixLabors.ImageSharp.Formats.IImageEncoder GetEncoderForExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => new JpegEncoder { Quality = 90 },
        ".png" => new PngEncoder(),
        ".bmp" => new BmpEncoder(),
        _ => new WebpEncoder { Quality = 85 }
    };
}
