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
            SupportedInputFormats = new[] { ".txt", ".json", ".csv", ".xml", ".yaml", ".yml", ".md", ".log", ".sql", ".js", ".ts", ".cs", ".py", ".html", ".css" },
            SupportedOutputFormats = new[] { ".txt", ".json", ".csv", ".sha256", ".md5", ".base64" },
            SupportedCategories = new[]
            {
                OperationCategory.Convert,
                OperationCategory.Edit,
                OperationCategory.Analyze,
                OperationCategory.Secure,
                OperationCategory.Optimize
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

                progress?.Report(new OperationProgress
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
                string op = request.OperationId.ToLowerInvariant();

                if (op.Contains("sha256") || op.Contains("hash") && !op.Contains("md5"))
                {
                    using var stream = File.OpenRead(file.FilePath);
                    var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
                    var hashStr = Convert.ToHexString(hashBytes);
                    var hashFile = Path.Combine(dir, $"{file.FileName}.sha256");
                    await File.WriteAllTextAsync(hashFile, $"{hashStr} *{file.FileName}\n", cancellationToken);
                    outputFiles.Add(hashFile);
                }
                else if (op.Contains("md5"))
                {
                    using var stream = File.OpenRead(file.FilePath);
                    var hashBytes = await MD5.HashDataAsync(stream, cancellationToken);
                    var hashStr = Convert.ToHexString(hashBytes);
                    var hashFile = Path.Combine(dir, $"{file.FileName}.md5");
                    await File.WriteAllTextAsync(hashFile, $"{hashStr} *{file.FileName}\n", cancellationToken);
                    outputFiles.Add(hashFile);
                }
                else if (op.Contains("format") || op.Contains("prettify"))
                {
                    if (file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        var content = await File.ReadAllTextAsync(file.FilePath, cancellationToken);
                        using var doc = JsonDocument.Parse(content);
                        var formatted = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                        var outPath = Path.Combine(dir, $"{baseName}_formatted.json");
                        await File.WriteAllTextAsync(outPath, formatted, cancellationToken);
                        outputFiles.Add(outPath);
                    }
                    else
                    {
                        // Clean whitespace format
                        var lines = await File.ReadAllLinesAsync(file.FilePath, cancellationToken);
                        var cleaned = new StringBuilder();
                        foreach (var line in lines)
                        {
                            cleaned.AppendLine(line.TrimEnd());
                        }
                        var outPath = Path.Combine(dir, $"{baseName}_formatted{file.Extension}");
                        await File.WriteAllTextAsync(outPath, cleaned.ToString(), cancellationToken);
                        outputFiles.Add(outPath);
                    }
                }
                else if (op.Contains("minify"))
                {
                    if (file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        var content = await File.ReadAllTextAsync(file.FilePath, cancellationToken);
                        using var doc = JsonDocument.Parse(content);
                        var minified = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = false });
                        var outPath = Path.Combine(dir, $"{baseName}_minified.json");
                        await File.WriteAllTextAsync(outPath, minified, cancellationToken);
                        outputFiles.Add(outPath);
                    }
                    else
                    {
                        var content = await File.ReadAllTextAsync(file.FilePath, cancellationToken);
                        var minified = System.Text.RegularExpressions.Regex.Replace(content, @"\s+", " ").Trim();
                        var outPath = Path.Combine(dir, $"{baseName}_minified{file.Extension}");
                        await File.WriteAllTextAsync(outPath, minified, cancellationToken);
                        outputFiles.Add(outPath);
                    }
                }
                else if (op.Contains("sort"))
                {
                    var lines = await File.ReadAllLinesAsync(file.FilePath, cancellationToken);
                    Array.Sort(lines, StringComparer.OrdinalIgnoreCase);
                    var sortedPath = Path.Combine(dir, $"{baseName}_sorted{file.Extension}");
                    await File.WriteAllLinesAsync(sortedPath, lines, cancellationToken);
                    outputFiles.Add(sortedPath);
                }
                else if (op.Contains("base64-encode") || op.Contains("to-base64"))
                {
                    var bytes = await File.ReadAllBytesAsync(file.FilePath, cancellationToken);
                    var base64 = Convert.ToBase64String(bytes);
                    var b64Path = Path.Combine(dir, $"{file.FileName}.base64");
                    await File.WriteAllTextAsync(b64Path, base64, cancellationToken);
                    outputFiles.Add(b64Path);
                }
                else if (op.Contains("base64-decode"))
                {
                    var text = (await File.ReadAllTextAsync(file.FilePath, cancellationToken)).Trim();
                    var bytes = Convert.FromBase64String(text);
                    var decodedPath = Path.Combine(dir, $"{baseName}_decoded.bin");
                    await File.WriteAllBytesAsync(decodedPath, bytes, cancellationToken);
                    outputFiles.Add(decodedPath);
                }
                else if (op.Contains("crlf") || op.Contains("lf"))
                {
                    var text = await File.ReadAllTextAsync(file.FilePath, cancellationToken);
                    string converted = op.Contains("to-lf") 
                        ? text.Replace("\r\n", "\n") 
                        : text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                    var lineEndingPath = Path.Combine(dir, $"{baseName}_normalized{file.Extension}");
                    await File.WriteAllTextAsync(lineEndingPath, converted, cancellationToken);
                    outputFiles.Add(lineEndingPath);
                }
                else if (op.Contains("count") || op.Contains("metrics") || op.Contains("analyze"))
                {
                    var text = await File.ReadAllTextAsync(file.FilePath, cancellationToken);
                    var lines = text.Split('\n');
                    var words = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    var stats = new
                    {
                        FileName = file.FileName,
                        FileSize = file.FileSize,
                        TotalLines = lines.Length,
                        TotalWords = words.Length,
                        TotalCharacters = text.Length,
                        NonWhitespaceCharacters = text.Count(c => !char.IsWhiteSpace(c))
                    };
                    var statsJson = JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true });
                    var statsPath = Path.Combine(dir, $"{baseName}_metrics.json");
                    await File.WriteAllTextAsync(statsPath, statsJson, cancellationToken);
                    outputFiles.Add(statsPath);
                }
                else
                {
                    throw new NotSupportedException($"Operation '{request.OperationId}' is not supported by TextDataProcessingEngine.");
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
