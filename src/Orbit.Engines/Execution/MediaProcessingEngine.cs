namespace Orbit.Engines.Execution;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core.Models;
using Orbit.Core.Services;

public class MediaProcessingEngine : IProcessingEngine
{
    public string EngineId => "media";
    public string DisplayName => "Orbit Media & Audio/Video Engine";
    public bool IsAvailable => true;

    private static readonly string[] VideoFormats = [".mp4", ".m4v", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".ts", ".3gp"];
    private static readonly string[] AudioFormats = [".mp3", ".wav", ".flac", ".aac", ".ogg", ".oga", ".m4a", ".wma", ".alac", ".opus"];
    private static readonly string[] AllMediaFormats = VideoFormats.Concat(AudioFormats).ToArray();

    public Task<EngineCapabilities> GetCapabilitiesAsync()
    {
        return Task.FromResult(new EngineCapabilities
        {
            EngineId = EngineId,
            SupportedInputFormats = AllMediaFormats,
            SupportedOutputFormats = new[] { ".mp4", ".webm", ".gif", ".mp3", ".wav", ".flac" },
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
        return Task.FromResult(request.InputFiles != null && request.InputFiles.Length > 0);
    }

    public static string? FindFfmpegExecutable()
    {
        // 1. Check relative tools directory
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var localPaths = new[]
        {
            Path.Combine(appDir, "tools", "ffmpeg", "bin", "ffmpeg.exe"),
            Path.Combine(appDir, "tools", "ffmpeg.exe"),
            Path.Combine(appDir, "ffmpeg.exe"),
            Path.Combine(Environment.CurrentDirectory, "tools", "ffmpeg", "bin", "ffmpeg.exe"),
            Path.Combine(Environment.CurrentDirectory, "tools", "ffmpeg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orbit", "tools", "ffmpeg", "bin", "ffmpeg.exe"),
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe"
        };

        foreach (var path in localPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        // 2. Check system PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var segment in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(segment.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public static string? FindFfprobeExecutable()
    {
        var ffmpeg = FindFfmpegExecutable();
        if (!string.IsNullOrEmpty(ffmpeg))
        {
            var dir = Path.GetDirectoryName(ffmpeg);
            if (!string.IsNullOrEmpty(dir))
            {
                var probeCandidate = Path.Combine(dir, "ffprobe.exe");
                if (File.Exists(probeCandidate))
                {
                    return probeCandidate;
                }
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var segment in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(segment.Trim(), "ffprobe.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public async Task<OperationResult> ExecuteAsync(
        OperationRequest request,
        IProgress<OperationProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var outputFiles = new List<string>();
        var metadata = new Dictionary<string, object>();

        try
        {
            int total = request.InputFiles.Length;
            var ffmpegPath = FindFfmpegExecutable();
            var normalizedOp = request.OperationId.ToLowerInvariant();

            // Operations that can analyze without ffmpeg as fallback:
            bool isAnalysisOp = normalizedOp is "media-analyze-info" or "media-inspect-streams";

            if (string.IsNullOrEmpty(ffmpegPath) && !isAnalysisOp)
            {
                stopwatch.Stop();
                return new OperationResult
                {
                    OperationId = request.OperationId,
                    Success = false,
                    ErrorMessage = "FFmpeg was not detected on system PATH or in ./tools/ffmpeg/bin/ffmpeg.exe. " +
                                   "To enable high-performance media transcoding and audio extraction, please install FFmpeg " +
                                   "(e.g., run 'winget install Gyan.FFmpeg' in PowerShell) or place ffmpeg.exe into './tools/ffmpeg/bin/'.",
                    Duration = stopwatch.Elapsed
                };
            }

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
                    StatusMessage = $"Processing media operation {request.OperationId} on {file.FileName}..."
                });

                var dir = request.OutputDirectory ?? Path.GetDirectoryName(file.FilePath) ?? Environment.CurrentDirectory;
                Directory.CreateDirectory(dir);
                var baseName = Path.GetFileNameWithoutExtension(file.FilePath);

                switch (normalizedOp)
                {
                    case "video-convert-mp4":
                    case "media-video-to-mp4":
                    {
                        var outPath = Path.Combine(dir, $"{baseName}.mp4");
                        if (string.Equals(outPath, file.FilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            outPath = Path.Combine(dir, $"{baseName}_converted.mp4");
                        }
                        var args = $"-y -i \"{file.FilePath}\" -c:v libx264 -preset fast -crf 22 -c:a aac -b:a 192k -movflags +faststart \"{outPath}\"";
                        await RunFfmpegProcessAsync(ffmpegPath!, args, cancellationToken);
                        outputFiles.Add(outPath);
                        break;
                    }

                    case "video-convert-webm":
                    case "media-video-to-webm":
                    {
                        var outPath = Path.Combine(dir, $"{baseName}.webm");
                        if (string.Equals(outPath, file.FilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            outPath = Path.Combine(dir, $"{baseName}_converted.webm");
                        }
                        var args = $"-y -i \"{file.FilePath}\" -c:v libvpx-vp9 -b:v 0 -crf 30 -c:a libopus \"{outPath}\"";
                        await RunFfmpegProcessAsync(ffmpegPath!, args, cancellationToken);
                        outputFiles.Add(outPath);
                        break;
                    }

                    case "video-extract-audio":
                    case "media-extract-audio-mp3":
                    {
                        var outPath = Path.Combine(dir, $"{baseName}.mp3");
                        var args = $"-y -i \"{file.FilePath}\" -vn -c:a libmp3lame -q:a 2 \"{outPath}\"";
                        await RunFfmpegProcessAsync(ffmpegPath!, args, cancellationToken);
                        outputFiles.Add(outPath);
                        break;
                    }

                    case "media-extract-audio-wav":
                    case "media-audio-to-wav":
                    {
                        var outPath = Path.Combine(dir, $"{baseName}.wav");
                        var args = $"-y -i \"{file.FilePath}\" -vn -c:a pcm_s16le \"{outPath}\"";
                        await RunFfmpegProcessAsync(ffmpegPath!, args, cancellationToken);
                        outputFiles.Add(outPath);
                        break;
                    }

                    case "media-audio-to-mp3":
                    {
                        var outPath = Path.Combine(dir, $"{baseName}.mp3");
                        var args = $"-y -i \"{file.FilePath}\" -c:a libmp3lame -q:a 2 \"{outPath}\"";
                        await RunFfmpegProcessAsync(ffmpegPath!, args, cancellationToken);
                        outputFiles.Add(outPath);
                        break;
                    }

                    case "media-audio-to-flac":
                    {
                        var outPath = Path.Combine(dir, $"{baseName}.flac");
                        var args = $"-y -i \"{file.FilePath}\" -c:a flac \"{outPath}\"";
                        await RunFfmpegProcessAsync(ffmpegPath!, args, cancellationToken);
                        outputFiles.Add(outPath);
                        break;
                    }

                    case "video-convert-gif":
                    case "media-video-to-gif":
                    {
                        var outPath = Path.Combine(dir, $"{baseName}.gif");
                        var args = $"-y -i \"{file.FilePath}\" -vf \"fps=10,scale=480:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" -loop 0 \"{outPath}\"";
                        await RunFfmpegProcessAsync(ffmpegPath!, args, cancellationToken);
                        outputFiles.Add(outPath);
                        break;
                    }

                    case "video-edit-trim":
                    case "media-trim-video":
                    {
                        var start = request.Parameters.TryGetValue("startTime", out var s) ? s.ToString() : "00:00:00";
                        var end = request.Parameters.TryGetValue("endTime", out var e) ? e.ToString() : "00:00:10";
                        var outPath = Path.Combine(dir, $"{baseName}_trimmed{file.Extension}");
                        var args = $"-y -ss {start} -to {end} -i \"{file.FilePath}\" -c copy \"{outPath}\"";
                        await RunFfmpegProcessAsync(ffmpegPath!, args, cancellationToken);
                        outputFiles.Add(outPath);
                        break;
                    }

                    case "media-compress-video":
                    {
                        var outPath = Path.Combine(dir, $"{baseName}_compressed.mp4");
                        var args = $"-y -i \"{file.FilePath}\" -c:v libx264 -crf 28 -preset medium -c:a aac -b:a 128k \"{outPath}\"";
                        await RunFfmpegProcessAsync(ffmpegPath!, args, cancellationToken);
                        outputFiles.Add(outPath);
                        break;
                    }

                    case "media-compress-audio":
                    {
                        var outPath = Path.Combine(dir, $"{baseName}_compressed.mp3");
                        var args = $"-y -i \"{file.FilePath}\" -c:a libmp3lame -b:a 128k \"{outPath}\"";
                        await RunFfmpegProcessAsync(ffmpegPath!, args, cancellationToken);
                        outputFiles.Add(outPath);
                        break;
                    }

                    case "media-mute-audio":
                    {
                        var outPath = Path.Combine(dir, $"{baseName}_muted{file.Extension}");
                        var args = $"-y -i \"{file.FilePath}\" -c:v copy -an \"{outPath}\"";
                        await RunFfmpegProcessAsync(ffmpegPath!, args, cancellationToken);
                        outputFiles.Add(outPath);
                        break;
                    }

                    case "media-adjust-volume":
                    {
                        var outPath = Path.Combine(dir, $"{baseName}_boosted{file.Extension}");
                        var args = $"-y -i \"{file.FilePath}\" -filter:a \"volume=1.5\" -c:v copy \"{outPath}\"";
                        await RunFfmpegProcessAsync(ffmpegPath!, args, cancellationToken);
                        outputFiles.Add(outPath);
                        break;
                    }

                    case "media-resize-resolution":
                    {
                        var outPath = Path.Combine(dir, $"{baseName}_720p{file.Extension}");
                        var args = $"-y -i \"{file.FilePath}\" -vf \"scale=-2:720\" -c:v libx264 -crf 23 -c:a copy \"{outPath}\"";
                        await RunFfmpegProcessAsync(ffmpegPath!, args, cancellationToken);
                        outputFiles.Add(outPath);
                        break;
                    }

                    case "media-analyze-info":
                    case "media-inspect-streams":
                    {
                        var (reportPath, fileMeta) = await AnalyzeMediaAsync(file.FilePath, ffmpegPath, cancellationToken);
                        outputFiles.Add(reportPath);
                        foreach (var kvp in fileMeta)
                        {
                            metadata[$"{file.FileName}_{kvp.Key}"] = kvp.Value;
                        }
                        break;
                    }

                    default:
                    {
                        // Fallback: If format specified, try to transcode
                        var targetExt = !string.IsNullOrEmpty(request.OutputFormat)
                            ? (request.OutputFormat.StartsWith(".") ? request.OutputFormat : "." + request.OutputFormat)
                            : ".mp4";

                        var outPath = Path.Combine(dir, $"{baseName}{targetExt}");
                        var args = $"-y -i \"{file.FilePath}\" \"{outPath}\"";
                        await RunFfmpegProcessAsync(ffmpegPath!, args, cancellationToken);
                        outputFiles.Add(outPath);
                        break;
                    }
                }
            }

            stopwatch.Stop();
            return new OperationResult
            {
                OperationId = request.OperationId,
                Success = true,
                OutputFiles = outputFiles.Distinct().ToArray(),
                Duration = stopwatch.Elapsed,
                ResultMetadata = metadata
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
                Duration = stopwatch.Elapsed,
                ResultMetadata = metadata
            };
        }
    }

    private static async Task RunFfmpegProcessAsync(string ffmpegPath, string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        var stderr = new StringBuilder();

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg failed with exit code {process.ExitCode}: {stderr}");
        }
    }

    private static async Task<(string ReportPath, Dictionary<string, object> Metadata)> AnalyzeMediaAsync(
        string mediaPath,
        string? ffmpegPath,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object>();
        var dir = Path.GetDirectoryName(mediaPath) ?? Environment.CurrentDirectory;
        var baseName = Path.GetFileNameWithoutExtension(mediaPath);
        var reportPath = Path.Combine(dir, $"{baseName}_media_analysis.txt");

        var fileInfo = new FileInfo(mediaPath);
        metadata["FileSize"] = fileInfo.Length;
        metadata["Extension"] = fileInfo.Extension;

        var report = new StringBuilder();
        report.AppendLine("=================================================");
        report.AppendLine(" Orbit Media Stream Analysis");
        report.AppendLine("=================================================");
        report.AppendLine($"File Name:           {Path.GetFileName(mediaPath)}");
        report.AppendLine($"File Size:           {fileInfo.Length:N0} bytes ({Math.Round((double)fileInfo.Length / (1024 * 1024), 2)} MB)");

        if (!string.IsNullOrEmpty(ffmpegPath))
        {
            // Execute ffprobe or ffmpeg -i to inspect streams
            var probePath = FindFfprobeExecutable();
            string toolToUse = !string.IsNullOrEmpty(probePath) ? probePath : ffmpegPath;
            string toolArgs = !string.IsNullOrEmpty(probePath)
                ? $"-v error -show_format -show_streams \"{mediaPath}\""
                : $"-i \"{mediaPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = toolToUse,
                Arguments = toolArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            var output = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            var probeText = output.ToString();

            // Extract duration: Duration: 00:01:23.45
            var durMatch = Regex.Match(probeText, @"Duration:\s*(\d+:\d+:\d+(?:\.\d+)?)");
            if (durMatch.Success)
            {
                metadata["Duration"] = durMatch.Groups[1].Value;
                report.AppendLine($"Duration:            {durMatch.Groups[1].Value}");
            }

            // Extract resolution: e.g. 1920x1080
            var resMatch = Regex.Match(probeText, @"(\d{3,4})x(\d{3,4})");
            if (resMatch.Success)
            {
                metadata["Resolution"] = $"{resMatch.Groups[1].Value}x{resMatch.Groups[2].Value}";
                report.AppendLine($"Resolution:          {resMatch.Groups[1].Value}x{resMatch.Groups[2].Value}");
            }

            // Extract bitrate: e.g. bitrate: 4500 kb/s
            var bitMatch = Regex.Match(probeText, @"bitrate:\s*(\d+\s*kb/s)");
            if (bitMatch.Success)
            {
                metadata["Bitrate"] = bitMatch.Groups[1].Value;
                report.AppendLine($"Bitrate:             {bitMatch.Groups[1].Value}");
            }

            // Extract video codec: Video: h264 ...
            var vCodecMatch = Regex.Match(probeText, @"Video:\s*([^,\n]+)");
            if (vCodecMatch.Success)
            {
                metadata["VideoCodec"] = vCodecMatch.Groups[1].Value.Trim();
                report.AppendLine($"Video Codec:         {vCodecMatch.Groups[1].Value.Trim()}");
            }

            // Extract audio codec: Audio: aac ...
            var aCodecMatch = Regex.Match(probeText, @"Audio:\s*([^,\n]+)");
            if (aCodecMatch.Success)
            {
                metadata["AudioCodec"] = aCodecMatch.Groups[1].Value.Trim();
                report.AppendLine($"Audio Codec:         {aCodecMatch.Groups[1].Value.Trim()}");
            }

            report.AppendLine("-------------------------------------------------");
            report.AppendLine("FFmpeg Stream Headers:");
            var streamLines = probeText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.Contains("Stream #") || l.Contains("Duration:") || l.Contains("codec_name="))
                .Take(10);
            foreach (var l in streamLines)
            {
                report.AppendLine($"  {l.Trim()}");
            }
        }
        else
        {
            // Fallback file header inspection
            report.AppendLine("FFmpeg Tool:         Not installed / detected");
            report.AppendLine("Inspection Note:     Basic file header parsed. For complete codec, resolution, and audio stream breakdown, install FFmpeg.");

            // Read first 64 bytes to inspect magic bytes
            try
            {
                using var fs = new FileStream(mediaPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                byte[] header = new byte[64];
                int read = await fs.ReadAsync(header, cancellationToken);
                string magicAscii = Encoding.Latin1.GetString(header, 0, read);

                if (magicAscii.Contains("ftyp"))
                {
                    metadata["Container"] = "ISO Base Media / MP4";
                    report.AppendLine("Container:           ISO Base Media / MP4 (ftyp atom)");
                }
                else if (magicAscii.StartsWith("RIFF"))
                {
                    metadata["Container"] = "RIFF (WAV / AVI)";
                    report.AppendLine("Container:           RIFF Container");
                }
                else if (magicAscii.StartsWith("ID3"))
                {
                    metadata["Container"] = "MP3 Audio (ID3v2)";
                    report.AppendLine("Container:           MP3 Audio with ID3v2 metadata");
                }
                else if (magicAscii.StartsWith("OggS"))
                {
                    metadata["Container"] = "Ogg Container";
                    report.AppendLine("Container:           Ogg Vorbis/Opus Container");
                }
                else if (magicAscii.StartsWith("fLaC"))
                {
                    metadata["Container"] = "FLAC Lossless Audio";
                    report.AppendLine("Container:           Free Lossless Audio Codec (FLAC)");
                }
            }
            catch { }
        }

        report.AppendLine("=================================================");
        await File.WriteAllTextAsync(reportPath, report.ToString(), cancellationToken);
        metadata["Report"] = report.ToString();

        return (reportPath, metadata);
    }
}
