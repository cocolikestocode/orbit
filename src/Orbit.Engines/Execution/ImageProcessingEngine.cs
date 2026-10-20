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
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
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
            SupportedInputFormats = new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tiff", ".tif", ".jfif" },
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
                progress?.Report(new OperationProgress
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
        var sourceExt = Path.GetExtension(file.FilePath).ToLowerInvariant();

        using var image = await Image.LoadAsync(file.FilePath, cancellationToken);

        string op = operationId.ToLowerInvariant();
        string targetExt = sourceExt;
        SixLabors.ImageSharp.Formats.IImageEncoder encoder;

        // 1. Analyze / Info
        if (op.Contains("analyze") || op.Contains("info") || op.Contains("exif"))
        {
            var infoPath = Path.Combine(dir, $"{nameWithoutExt}_image_info.json");
            var infoJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                FileName = file.FileName,
                Width = image.Width,
                Height = image.Height,
                AspectRatio = (double)image.Width / Math.Max(1, image.Height),
                ColorType = image.PixelType.BitsPerPixel + " bits",
                FileSize = file.FileSize,
                Format = image.Metadata.DecodedImageFormat?.Name ?? sourceExt.TrimStart('.').ToUpperInvariant()
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(infoPath, infoJson, cancellationToken);
            return infoPath;
        }

        // 2. Transformations (Rotate, Flip, Resize, Strip)
        if (op.Contains("rotate-180"))
        {
            image.Mutate(x => x.Rotate(180));
            nameWithoutExt += "_rotated180";
        }
        else if (op.Contains("rotate-270"))
        {
            image.Mutate(x => x.Rotate(270));
            nameWithoutExt += "_rotated270";
        }
        else if (op.Contains("rotate"))
        {
            image.Mutate(x => x.Rotate(90));
            nameWithoutExt += "_rotated90";
        }

        if (op.Contains("flip-v") || op.Contains("flip-vertical"))
        {
            image.Mutate(x => x.Flip(FlipMode.Vertical));
            nameWithoutExt += "_flipped_v";
        }
        else if (op.Contains("flip"))
        {
            image.Mutate(x => x.Flip(FlipMode.Horizontal));
            nameWithoutExt += "_flipped_h";
        }

        if (op.Contains("resize-50"))
        {
            image.Mutate(x => x.Resize(Math.Max(1, image.Width / 2), Math.Max(1, image.Height / 2)));
            nameWithoutExt += "_half";
        }
        else if (op.Contains("resize-25"))
        {
            image.Mutate(x => x.Resize(Math.Max(1, image.Width / 4), Math.Max(1, image.Height / 4)));
            nameWithoutExt += "_quarter";
        }
        else if (op.Contains("resize-75"))
        {
            image.Mutate(x => x.Resize(Math.Max(1, (int)(image.Width * 0.75)), Math.Max(1, (int)(image.Height * 0.75))));
            nameWithoutExt += "_75pct";
        }

        if (op.Contains("strip") || op.Contains("metadata"))
        {
            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;
            nameWithoutExt += "_clean";
        }

        // 3. Format determination
        bool toJpg = op.EndsWith("to-jpg") || op.EndsWith("to-jpeg") || op.Contains("convert-jpg") || op.Contains("-jpg") ||
                     string.Equals(explicitOutputFormat, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(explicitOutputFormat, ".jpeg", StringComparison.OrdinalIgnoreCase);

        bool toWebp = op.EndsWith("to-webp") || op.Contains("convert-webp") || op.Contains("-webp") ||
                      string.Equals(explicitOutputFormat, ".webp", StringComparison.OrdinalIgnoreCase);

        bool toPng = op.EndsWith("to-png") || op.Contains("convert-png") || op.Contains("-png") ||
                     string.Equals(explicitOutputFormat, ".png", StringComparison.OrdinalIgnoreCase);

        bool toBmp = op.EndsWith("to-bmp") || op.Contains("convert-bmp") || op.Contains("-bmp") ||
                     string.Equals(explicitOutputFormat, ".bmp", StringComparison.OrdinalIgnoreCase);

        bool isOptimize = op.Contains("optimize") || op.Contains("compress");

        if (toJpg)
        {
            targetExt = ".jpg";
            encoder = new JpegEncoder { Quality = 90 };
        }
        else if (toWebp)
        {
            targetExt = ".webp";
            encoder = new WebpEncoder { Quality = 85 };
        }
        else if (toPng)
        {
            targetExt = ".png";
            encoder = new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression };
        }
        else if (toBmp)
        {
            targetExt = ".bmp";
            encoder = new BmpEncoder();
        }
        else if (isOptimize)
        {
            targetExt = sourceExt switch
            {
                ".png" => ".png",
                ".webp" => ".webp",
                _ => ".jpg"
            };
            encoder = targetExt switch
            {
                ".png" => new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression },
                ".webp" => new WebpEncoder { Quality = 75 },
                _ => new JpegEncoder { Quality = 75 }
            };
            nameWithoutExt += "_optimized";
        }
        else
        {
            targetExt = sourceExt;
            encoder = GetEncoderForExtension(targetExt);
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
        ".jpg" or ".jpeg" or ".jfif" => new JpegEncoder { Quality = 90 },
        ".png" => new PngEncoder { CompressionLevel = PngCompressionLevel.DefaultCompression },
        ".bmp" => new BmpEncoder(),
        _ => new WebpEncoder { Quality = 85 }
    };
}
