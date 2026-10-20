namespace Orbit.Engines.Execution;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core.Models;
using Orbit.Core.Services;

public class TextDataProcessingEngine : IProcessingEngine
{
    public string EngineId => "text";
    public string DisplayName => "Orbit Text & Data Engine";
    public bool IsAvailable => true;

    public Task<EngineCapabilities> GetCapabilitiesAsync()
    {
        return Task.FromResult(new EngineCapabilities
        {
            EngineId = EngineId,
            SupportedInputFormats = new[] { ".txt", ".json", ".csv", ".xml", ".yaml", ".md", ".log", ".sql", ".js", ".ts", ".cs", ".py" },
            SupportedOutputFormats = new[] { ".txt", ".json", ".csv" },
            SupportedCategories = new[]
            {
                OperationCategory.Convert,
                OperationCategory.Edit,
                OperationCategory.Analyze,
                OperationCategory.Secure
            },
            SupportsBatch = true
        });
    }

    public Task<bool> ValidateAsync(OperationRequest request)
    {
        return Task.FromResult(request.InputFiles.Length > 0);
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

                var dir = Path.GetDirectoryName(file.FilePath) ?? Environment.CurrentDirectory;
                var baseName = Path.GetFileNameWithoutExtension(file.FilePath);

                switch (request.OperationId)
                {
                    case "text-secure-hash":
                    case "text-hash-sha256":
                        using (var stream = File.OpenRead(file.FilePath))
                        {
                            var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
                            var hashStr = Convert.ToHexString(hashBytes);
                            var hashFile = Path.Combine(dir, $"{file.FileName}.sha256");
                            await File.WriteAllTextAsync(hashFile, $"{hashStr} *{file.FileName}\n", cancellationToken);
                            outputFiles.Add(hashFile);
                        }
                        break;

                    case "text-edit-format":
                    case "text-format-json":
                        if (file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            var content = await File.ReadAllTextAsync(file.FilePath, cancellationToken);
                            using var doc = JsonDocument.Parse(content);
                            var formatted = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                            var outPath = Path.Combine(dir, $"{baseName}_formatted.json");
                            await File.WriteAllTextAsync(outPath, formatted, cancellationToken);
                            outputFiles.Add(outPath);
                        }
                        break;

                    case "text-edit-sort-lines":
                        var lines = await File.ReadAllLinesAsync(file.FilePath, cancellationToken);
                        Array.Sort(lines, StringComparer.OrdinalIgnoreCase);
                        var sortedPath = Path.Combine(dir, $"{baseName}_sorted{file.Extension}");
                        await File.WriteAllLinesAsync(sortedPath, lines, cancellationToken);
                        outputFiles.Add(sortedPath);
                        break;

                    default:
                        // General text summary output
                        var infoPath = Path.Combine(dir, $"{baseName}_info.txt");
                        var fileInfo = new FileInfo(file.FilePath);
                        var infoContent = $"Orbit File Analysis\nFile: {file.FileName}\nSize: {fileInfo.Length} bytes\nCreated: {fileInfo.CreationTimeUtc} UTC\nFamily: {file.Family}\n";
                        await File.WriteAllTextAsync(infoPath, infoContent, cancellationToken);
                        outputFiles.Add(infoPath);
                        break;
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
}
