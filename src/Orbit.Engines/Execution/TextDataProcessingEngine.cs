namespace Orbit.Engines.Execution;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Orbit.Core.Models;
using Orbit.Core.Services;

public class TextDataProcessingEngine : IProcessingEngine
{
    public string EngineId => "textdata";
    public string DisplayName => "Orbit Text & Data Engine";
    public bool IsAvailable => true;

    public Task<EngineCapabilities> GetCapabilitiesAsync()
    {
        return Task.FromResult(new EngineCapabilities
        {
            EngineId = EngineId,
            SupportedInputFormats = new[] { ".txt", ".json", ".csv", ".xml", ".yaml", ".yml", ".md", ".log", ".sql", ".js", ".ts", ".cs", ".py", ".html", ".css", ".tsv" },
            SupportedOutputFormats = new[] { ".txt", ".json", ".csv", ".sha256", ".md5", ".base64", ".yaml", ".xml" },
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
        return Task.FromResult(request.InputFiles != null && request.InputFiles.Length > 0);
    }

    public async Task<OperationResult> ExecuteAsync(
        OperationRequest request,
        IProgress<OperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var outputFiles = new List<string>();

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

                var dir = request.OutputDirectory ?? Path.GetDirectoryName(file.FilePath) ?? Environment.CurrentDirectory;
                Directory.CreateDirectory(dir);
                var baseName = Path.GetFileNameWithoutExtension(file.FilePath);
                string op = request.OperationId.ToLowerInvariant();

                // 1. JSON to CSV
                if (op.Contains("json-to-csv"))
                {
                    var text = await File.ReadAllTextAsync(file.FilePath, cancellationToken);
                    using var doc = JsonDocument.Parse(text);
                    var sb = new StringBuilder();

                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var headers = new List<string>();
                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var prop in item.EnumerateObject())
                                {
                                    if (!headers.Contains(prop.Name)) headers.Add(prop.Name);
                                }
                            }
                        }
                        sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.Object)
                            {
                                var row = headers.Select(h => item.TryGetProperty(h, out var v) ? EscapeCsv(v.ToString()) : "");
                                sb.AppendLine(string.Join(",", row));
                            }
                        }
                    }
                    else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        sb.AppendLine("Key,Value");
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            sb.AppendLine($"{EscapeCsv(prop.Name)},{EscapeCsv(prop.Value.ToString())}");
                        }
                    }

                    var csvPath = Path.Combine(dir, $"{baseName}.csv");
                    await File.WriteAllTextAsync(csvPath, sb.ToString(), cancellationToken);
                    outputFiles.Add(csvPath);
                }
                // 2. CSV to JSON
                else if (op.Contains("csv-to-json"))
                {
                    var lines = await File.ReadAllLinesAsync(file.FilePath, cancellationToken);
                    if (lines.Length > 0)
                    {
                        var headers = ParseCsvLine(lines[0]);
                        var rows = new List<Dictionary<string, string>>();
                        for (int r = 1; r < lines.Length; r++)
                        {
                            if (string.IsNullOrWhiteSpace(lines[r])) continue;
                            var cols = ParseCsvLine(lines[r]);
                            var dict = new Dictionary<string, string>();
                            for (int c = 0; c < headers.Count; c++)
                            {
                                dict[headers[c]] = c < cols.Count ? cols[c] : "";
                            }
                            rows.Add(dict);
                        }
                        var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
                        var jsonPath = Path.Combine(dir, $"{baseName}.json");
                        await File.WriteAllTextAsync(jsonPath, json, cancellationToken);
                        outputFiles.Add(jsonPath);
                    }
                }
                // 3. JSON to YAML
                else if (op.Contains("json-to-yaml"))
                {
                    var text = await File.ReadAllTextAsync(file.FilePath, cancellationToken);
                    using var doc = JsonDocument.Parse(text);
                    var sb = new StringBuilder();
                    WriteJsonElementToYaml(doc.RootElement, sb, 0);
                    var yamlPath = Path.Combine(dir, $"{baseName}.yaml");
                    await File.WriteAllTextAsync(yamlPath, sb.ToString(), cancellationToken);
                    outputFiles.Add(yamlPath);
                }
                // 4. YAML to JSON
                else if (op.Contains("yaml-to-json"))
                {
                    var yamlLines = await File.ReadAllLinesAsync(file.FilePath, cancellationToken);
                    var dict = new Dictionary<string, string>();
                    foreach (var line in yamlLines)
                    {
                        var trimmed = line.Trim();
                        int colonIdx = trimmed.IndexOf(':');
                        if (colonIdx > 0)
                        {
                            var k = trimmed.Substring(0, colonIdx).Trim();
                            var v = trimmed.Substring(colonIdx + 1).Trim().Trim('"', '\'');
                            dict[k] = v;
                        }
                    }
                    var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                    var jsonPath = Path.Combine(dir, $"{baseName}.json");
                    await File.WriteAllTextAsync(jsonPath, json, cancellationToken);
                    outputFiles.Add(jsonPath);
                }
                // 5. XML to JSON
                else if (op.Contains("xml-to-json"))
                {
                    var xml = await File.ReadAllTextAsync(file.FilePath, cancellationToken);
                    var xdoc = XDocument.Parse(xml);
                    var dict = ConvertXElementToDict(xdoc.Root);
                    var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                    var jsonPath = Path.Combine(dir, $"{baseName}.json");
                    await File.WriteAllTextAsync(jsonPath, json, cancellationToken);
                    outputFiles.Add(jsonPath);
                }
                // 6. Format XML
                else if (op.Contains("format-xml"))
                {
                    var xml = await File.ReadAllTextAsync(file.FilePath, cancellationToken);
                    var xdoc = XDocument.Parse(xml);
                    var outPath = Path.Combine(dir, $"{baseName}_formatted.xml");
                    await File.WriteAllTextAsync(outPath, xdoc.ToString(), cancellationToken);
                    outputFiles.Add(outPath);
                }
                // 7. Checksum / Hashes
                else if (op.Contains("checksum") || op.Contains("sha256") || op.Contains("hash") && !op.Contains("md5"))
                {
                    using var stream = File.OpenRead(file.FilePath);
                    var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
                    var hashStr = Convert.ToHexString(hashBytes).ToLowerInvariant();
                    var hashFile = Path.Combine(dir, $"{file.FileName}.sha256");
                    await File.WriteAllTextAsync(hashFile, $"{hashStr} *{file.FileName}\n", cancellationToken);
                    outputFiles.Add(hashFile);
                }
                else if (op.Contains("md5"))
                {
                    using var stream = File.OpenRead(file.FilePath);
                    var hashBytes = await MD5.HashDataAsync(stream, cancellationToken);
                    var hashStr = Convert.ToHexString(hashBytes).ToLowerInvariant();
                    var hashFile = Path.Combine(dir, $"{file.FileName}.md5");
                    await File.WriteAllTextAsync(hashFile, $"{hashStr} *{file.FileName}\n", cancellationToken);
                    outputFiles.Add(hashFile);
                }
                // 8. Format / Prettify JSON / Text
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
                // 9. Minify JSON / Text
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
                // 10. Sort Lines
                else if (op.Contains("sort"))
                {
                    var lines = await File.ReadAllLinesAsync(file.FilePath, cancellationToken);
                    Array.Sort(lines, StringComparer.OrdinalIgnoreCase);
                    var sortedPath = Path.Combine(dir, $"{baseName}_sorted{file.Extension}");
                    await File.WriteAllLinesAsync(sortedPath, lines, cancellationToken);
                    outputFiles.Add(sortedPath);
                }
                // 11. Base64 Encode / Decode
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
                // 12. Normalize Line Endings
                else if (op.Contains("line-endings") || op.Contains("crlf") || op.Contains("lf"))
                {
                    var text = await File.ReadAllTextAsync(file.FilePath, cancellationToken);
                    string converted = op.Contains("to-lf") || op.Contains("unix")
                        ? text.Replace("\r\n", "\n") 
                        : text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                    var lineEndingPath = Path.Combine(dir, $"{baseName}_normalized{file.Extension}");
                    await File.WriteAllTextAsync(lineEndingPath, converted, cancellationToken);
                    outputFiles.Add(lineEndingPath);
                }
                // 13. Metrics / Word Count / Diff
                else if (op.Contains("count") || op.Contains("metrics") || op.Contains("analyze") || op.Contains("diff"))
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
                    // Fallback to formatted output
                    var text = await File.ReadAllTextAsync(file.FilePath, cancellationToken);
                    var fallbackPath = Path.Combine(dir, $"{baseName}_processed{file.Extension}");
                    await File.WriteAllTextAsync(fallbackPath, text, cancellationToken);
                    outputFiles.Add(fallbackPath);
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

    private static string EscapeCsv(string str)
    {
        if (str.Contains(',') || str.Contains('"') || str.Contains('\n') || str.Contains('\r'))
        {
            return $"\"{str.Replace("\"", "\"\"")}\"";
        }
        return str;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var cur = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cur.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(cur.ToString().Trim());
                cur.Clear();
            }
            else
            {
                cur.Append(c);
            }
        }
        result.Add(cur.ToString().Trim());
        return result;
    }

    private static void WriteJsonElementToYaml(JsonElement element, StringBuilder sb, int indent)
    {
        string pad = new string(' ', indent);
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        sb.AppendLine($"{pad}{prop.Name}:");
                        WriteJsonElementToYaml(prop.Value, sb, indent + 2);
                    }
                    else
                    {
                        sb.AppendLine($"{pad}{prop.Name}: {prop.Value}");
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    sb.Append($"{pad}- ");
                    if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        sb.AppendLine();
                        WriteJsonElementToYaml(item, sb, indent + 2);
                    }
                    else
                    {
                        sb.AppendLine(item.ToString());
                    }
                }
                break;
            default:
                sb.AppendLine($"{pad}{element}");
                break;
        }
    }

    private static object? ConvertXElementToDict(XElement? el)
    {
        if (el == null) return null;
        if (!el.HasElements) return el.Value;

        var dict = new Dictionary<string, object>();
        foreach (var child in el.Elements())
        {
            dict[child.Name.LocalName] = ConvertXElementToDict(child)!;
        }
        return dict;
    }
}
