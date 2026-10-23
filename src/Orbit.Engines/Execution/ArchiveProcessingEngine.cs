namespace Orbit.Engines.Execution;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core.Models;
using Orbit.Core.Services;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Writers;

public class ArchiveProcessingEngine : IProcessingEngine
{
    public const long MaxTotalUncompressedBytes = 10L * 1024 * 1024 * 1024; // 10 GB limit
    public const int MaxTotalEntries = 100_000;
    public const double MaxCompressionRatio = 500.0;

    public string EngineId => "archive";
    public string DisplayName => "Orbit Native Archive Engine";
    public bool IsAvailable => true;

    public Task<EngineCapabilities> GetCapabilitiesAsync()
    {
        return Task.FromResult(new EngineCapabilities
        {
            EngineId = EngineId,
            SupportedInputFormats = new[]
            {
                ".zip", ".7z", ".tar", ".gz", ".bz2", ".xz", ".rar", ".gzip", ".bzip2", ".tgz"
            },
            SupportedOutputFormats = new[] { ".zip", ".7z", ".tar.gz" },
            SupportedCategories = new[]
            {
                OperationCategory.Extract,
                OperationCategory.Create,
                OperationCategory.Analyze,
                OperationCategory.Optimize,
                OperationCategory.Secure
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
        var metadata = new Dictionary<string, object>();

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
                    StatusMessage = $"Processing archive operation {request.OperationId} on {file.FileName}..."
                });

                var normalizedOp = request.OperationId.ToLowerInvariant();

                switch (normalizedOp)
                {
                    case "archive-extract-here":
                    case "archive-extract-all":
                    {
                        var targetDir = request.OutputDirectory ?? Path.GetDirectoryName(file.FilePath) ?? Environment.CurrentDirectory;
                        var extracted = await ExtractArchiveSafelyAsync(file.FilePath, targetDir, cancellationToken);
                        outputFiles.AddRange(extracted);
                        break;
                    }

                    case "archive-extract-folder":
                    case "archive-extract-to-folder":
                    {
                        var parentDir = request.OutputDirectory ?? Path.GetDirectoryName(file.FilePath) ?? Environment.CurrentDirectory;
                        var folderName = GetSafeArchiveFolderName(file.FilePath);
                        var targetDir = DetermineSmartExtractFolder(file.FilePath, parentDir, folderName);
                        Directory.CreateDirectory(targetDir);
                        var extracted = await ExtractArchiveSafelyAsync(file.FilePath, targetDir, cancellationToken);
                        outputFiles.AddRange(extracted);
                        if (!outputFiles.Contains(targetDir))
                        {
                            outputFiles.Insert(0, targetDir);
                        }
                        break;
                    }

                    case "archive-create-zip":
                    {
                        // In batch create-zip, all input files are packed together
                        var zipPath = await CreateZipArchiveAsync(request.InputFiles, request.OutputDirectory, cancellationToken);
                        outputFiles.Add(zipPath);
                        // Exit loop as all files have been bundled
                        i = total;
                        break;
                    }

                    case "archive-encrypt":
                    {
                        var pwd = request.Parameters.TryGetValue("password", out var pObj) && pObj is string s && !string.IsNullOrWhiteSpace(s)
                            ? s
                            : "OrbitSecure123";
                        var encPath = await CreateEncryptedArchiveAsync(request.InputFiles, pwd, request.OutputDirectory, cancellationToken);
                        outputFiles.Add(encPath);
                        i = total;
                        break;
                    }

                    case "archive-analyze-integrity":
                    case "archive-test-integrity":
                    case "archive-inspect-contents":
                    {
                        var analysisResult = await AnalyzeIntegrityAsync(file.FilePath, cancellationToken);
                        foreach (var kvp in analysisResult.Metadata)
                        {
                            metadata[$"{file.FileName}_{kvp.Key}"] = kvp.Value;
                        }
                        outputFiles.Add(analysisResult.ReportFilePath);
                        break;
                    }

                    default:
                    {
                        // Default fallback to extract
                        var targetDir = request.OutputDirectory ?? Path.GetDirectoryName(file.FilePath) ?? Environment.CurrentDirectory;
                        var extracted = await ExtractArchiveSafelyAsync(file.FilePath, targetDir, cancellationToken);
                        outputFiles.AddRange(extracted);
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

    public static async Task<List<string>> ExtractArchiveSafelyAsync(
        string archivePath,
        string targetDirectory,
        CancellationToken cancellationToken = default,
        long maxTotalBytes = MaxTotalUncompressedBytes,
        int maxEntries = MaxTotalEntries)
    {
        var extractedFiles = new List<string>();
        var fullTargetDir = Path.GetFullPath(targetDirectory);
        if (!fullTargetDir.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            fullTargetDir += Path.DirectorySeparatorChar;
        }

        Directory.CreateDirectory(fullTargetDir);

        using var archive = ArchiveFactory.Open(archivePath);
        long totalUncompressedBytes = 0;
        int entryCount = 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                continue;
            }

            entryCount++;
            if (entryCount > maxEntries)
            {
                throw new SecurityException($"Archive bomb detected: Archive contains more than {maxEntries} entries.");
            }

            // Normalize path separators
            var normalizedKey = entry.Key.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

            // Zip-Slip / Path Traversal Prevention
            var destinationPath = Path.GetFullPath(Path.Combine(fullTargetDir, normalizedKey));
            if (!destinationPath.StartsWith(fullTargetDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException(
                    $"Path traversal / Zip-Slip detected! Entry '{entry.Key}' attempts to escape destination directory '{fullTargetDir}'.");
            }

            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var entryDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(entryDir))
            {
                Directory.CreateDirectory(entryDir);
            }

            // Stream extraction with Archive Bomb bytes limit & compression ratio check
            long entryBytes = 0;
            using (var entryStream = entry.OpenEntryStream())
            using (var outStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                byte[] buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await entryStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    totalUncompressedBytes += bytesRead;
                    entryBytes += bytesRead;

                    if (totalUncompressedBytes > maxTotalBytes)
                    {
                        throw new SecurityException(
                            $"Archive bomb detected: Total uncompressed content exceeds the safety limit of {maxTotalBytes} bytes.");
                    }

                    if (entry.CompressedSize > 0 && entryBytes > 2 * 1024 * 1024)
                    {
                        double ratio = (double)entryBytes / entry.CompressedSize;
                        if (ratio > MaxCompressionRatio)
                        {
                            throw new SecurityException(
                                $"Archive bomb detected: Entry '{entry.Key}' compression ratio ({ratio:F1}:1) exceeds safety threshold of {MaxCompressionRatio}:1.");
                        }
                    }

                    await outStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }
            }

            extractedFiles.Add(destinationPath);
        }

        return extractedFiles;
    }

    private static string DetermineSmartExtractFolder(string archivePath, string parentDir, string defaultFolderName)
    {
        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            var nonDirEntries = archive.Entries.Where(e => !e.IsDirectory && !string.IsNullOrWhiteSpace(e.Key)).ToList();

            if (nonDirEntries.Count > 0)
            {
                // Inspect root directory component of each entry
                string? firstRoot = null;
                bool hasSingleRoot = true;

                foreach (var entry in nonDirEntries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                    var parts = entry.Key.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length <= 1)
                    {
                        // File at root of archive
                        hasSingleRoot = false;
                        break;
                    }

                    if (firstRoot == null)
                    {
                        firstRoot = parts[0];
                    }
                    else if (!string.Equals(firstRoot, parts[0], StringComparison.OrdinalIgnoreCase))
                    {
                        hasSingleRoot = false;
                        break;
                    }
                }

                // If all files are already enclosed in a single folder, unpack directly into parentDir
                if (hasSingleRoot && !string.IsNullOrEmpty(firstRoot))
                {
                    return parentDir;
                }
            }
        }
        catch
        {
            // Fallback to dedicated folder on inspection failure
        }

        var candidate = Path.Combine(parentDir, defaultFolderName);
        int counter = 1;
        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(parentDir, $"{defaultFolderName}_{counter}");
            counter++;
        }
        return candidate;
    }

    private static string GetSafeArchiveFolderName(string archivePath)
    {
        var fileName = Path.GetFileName(archivePath);
        var extensions = new[] { ".tar.gz", ".tar.bz2", ".tar.xz", ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".tgz" };

        foreach (var ext in extensions)
        {
            if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return fileName.Substring(0, fileName.Length - ext.Length);
            }
        }

        return Path.GetFileNameWithoutExtension(archivePath);
    }

    private static async Task<string> CreateZipArchiveAsync(
        FileIdentity[] files,
        string? outputDirectory,
        CancellationToken cancellationToken)
    {
        var firstPath = files.Length > 0 ? files[0].FilePath : null;
        var targetDir = outputDirectory ?? (!string.IsNullOrEmpty(firstPath) ? Path.GetDirectoryName(firstPath) : null) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(targetDir);

        string zipBaseName;
        if (files.Length == 1)
        {
            zipBaseName = Path.GetFileNameWithoutExtension(files[0].FilePath);
        }
        else
        {
            zipBaseName = $"Archive_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        var destinationZip = Path.Combine(targetDir, $"{zipBaseName}.zip");
        int counter = 1;
        while (File.Exists(destinationZip))
        {
            destinationZip = Path.Combine(targetDir, $"{zipBaseName}_{counter}.zip");
            counter++;
        }

        using var archive = ZipArchive.Create();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(file.FilePath))
            {
                archive.AddEntry(file.FileName, file.FilePath);
            }
            else if (Directory.Exists(file.FilePath))
            {
                AddDirectoryRecursive(archive, file.FilePath, file.FileName);
            }
        }

        using (var fileStream = new FileStream(destinationZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            archive.SaveTo(fileStream, new WriterOptions(CompressionType.Deflate));
            await fileStream.FlushAsync(cancellationToken);
        }

        return destinationZip;
    }

    private static async Task<string> CreateEncryptedArchiveAsync(
        FileIdentity[] files,
        string password,
        string? outputDirectory,
        CancellationToken cancellationToken)
    {
        var firstPath = files.Length > 0 ? files[0].FilePath : null;
        var targetDir = outputDirectory ?? (!string.IsNullOrEmpty(firstPath) ? Path.GetDirectoryName(firstPath) : null) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(targetDir);

        string zipBaseName;
        if (files.Length == 1)
        {
            zipBaseName = $"{Path.GetFileNameWithoutExtension(files[0].FilePath)}_encrypted";
        }
        else
        {
            zipBaseName = $"EncryptedArchive_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        var destinationZip = Path.Combine(targetDir, $"{zipBaseName}.zip");
        int counter = 1;
        while (File.Exists(destinationZip))
        {
            destinationZip = Path.Combine(targetDir, $"{zipBaseName}_{counter}.zip");
            counter++;
        }

        // Create standard zip package first
        using (var archive = ZipArchive.Create())
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(file.FilePath))
                {
                    archive.AddEntry(file.FileName, file.FilePath);
                }
                else if (Directory.Exists(file.FilePath))
                {
                    AddDirectoryRecursive(archive, file.FilePath, file.FileName);
                }
            }

            using (var fileStream = new FileStream(destinationZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                archive.SaveTo(fileStream, new WriterOptions(CompressionType.Deflate));
                await fileStream.FlushAsync(cancellationToken);
            }
        }

        // Now encrypt the archive using AES-256 standard encryption
        var encPath = Path.Combine(targetDir, $"{zipBaseName}.orbit.enc");
        await EncryptFileWithAesAsync(destinationZip, encPath, password, cancellationToken);
        
        // Remove unencrypted intermediate zip if different
        try { if (File.Exists(destinationZip)) File.Delete(destinationZip); } catch { }

        return encPath;
    }

    private static async Task EncryptFileWithAesAsync(string inPath, string outPath, string password, CancellationToken cancellationToken)
    {
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = System.Security.Cryptography.CipherMode.CBC;
        aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;

        byte[] salt = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(salt);

        using var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, 10000, System.Security.Cryptography.HashAlgorithmName.SHA256);
        aes.Key = pbkdf2.GetBytes(32);
        aes.IV = pbkdf2.GetBytes(16);

        using var outFs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
        // Write Magic Header + Salt + IV
        byte[] magic = System.Text.Encoding.ASCII.GetBytes("ORBITENC");
        await outFs.WriteAsync(magic, cancellationToken);
        await outFs.WriteAsync(salt, cancellationToken);
        await outFs.WriteAsync(aes.IV, cancellationToken);

        using var cryptoStream = new System.Security.Cryptography.CryptoStream(outFs, aes.CreateEncryptor(), System.Security.Cryptography.CryptoStreamMode.Write);
        using var inFs = new FileStream(inPath, FileMode.Open, FileAccess.Read);
        await inFs.CopyToAsync(cryptoStream, cancellationToken);
        await cryptoStream.FlushFinalBlockAsync(cancellationToken);
    }

    private static void AddDirectoryRecursive(ZipArchive archive, string sourceDir, string rootRelativePath)
    {
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var entryKey = Path.Combine(rootRelativePath, relative).Replace('\\', '/');
            archive.AddEntry(entryKey, file);
        }
    }

    private static async Task<(string ReportFilePath, Dictionary<string, object> Metadata)> AnalyzeIntegrityAsync(
        string archivePath,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object>();
        var dir = Path.GetDirectoryName(archivePath) ?? Environment.CurrentDirectory;
        var baseName = Path.GetFileNameWithoutExtension(archivePath);
        var reportPath = Path.Combine(dir, $"{baseName}_integrity.txt");

        int entryCount = 0;
        int fileCount = 0;
        int dirCount = 0;
        long totalUncompressed = 0;
        long totalCompressed = 0;
        bool integrityPassed = true;
        string? failureReason = null;
        var entrySamples = new List<string>();

        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            metadata["ArchiveType"] = archive.Type.ToString();

            byte[] dummyBuffer = new byte[81920];

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                entryCount++;

                if (entry.IsDirectory)
                {
                    dirCount++;
                }
                else
                {
                    fileCount++;
                    totalUncompressed += entry.Size;
                    totalCompressed += entry.CompressedSize;

                    if (entrySamples.Count < 15)
                    {
                        entrySamples.Add($"  [FILE] {entry.Key} ({entry.Size:N0} bytes)");
                    }

                    // Test entry stream reading to verify CRC
                    try
                    {
                        using var stream = entry.OpenEntryStream();
                        while (await stream.ReadAsync(dummyBuffer, 0, dummyBuffer.Length, cancellationToken) > 0)
                        {
                            // reading tests stream integrity and CRC checksum
                        }
                    }
                    catch (Exception streamEx)
                    {
                        integrityPassed = false;
                        failureReason = $"Entry '{entry.Key}' failed integrity check: {streamEx.Message}";
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            integrityPassed = false;
            failureReason = $"Failed to read archive: {ex.Message}";
        }

        double ratio = totalCompressed > 0 ? (double)totalUncompressed / totalCompressed : 1.0;

        metadata["EntryCount"] = entryCount;
        metadata["FileCount"] = fileCount;
        metadata["DirectoryCount"] = dirCount;
        metadata["TotalUncompressedBytes"] = totalUncompressed;
        metadata["TotalCompressedBytes"] = totalCompressed;
        metadata["CompressionRatio"] = Math.Round(ratio, 2);
        metadata["IntegrityPassed"] = integrityPassed;
        if (failureReason != null)
        {
            metadata["FailureReason"] = failureReason;
        }

        var reportContent = new System.Text.StringBuilder();
        reportContent.AppendLine("=================================================");
        reportContent.AppendLine(" Orbit Archive Analysis & Integrity Report");
        reportContent.AppendLine("=================================================");
        reportContent.AppendLine($"Archive:             {Path.GetFileName(archivePath)}");
        reportContent.AppendLine($"Integrity Status:    {(integrityPassed ? "PASSED (All checksums verified intact)" : "FAILED: " + failureReason)}");
        reportContent.AppendLine($"Total Entries:       {entryCount} ({fileCount} files, {dirCount} folders)");
        reportContent.AppendLine($"Uncompressed Size:   {totalUncompressed:N0} bytes ({Math.Round((double)totalUncompressed / (1024 * 1024), 2)} MB)");
        reportContent.AppendLine($"Compressed Size:     {totalCompressed:N0} bytes ({Math.Round((double)totalCompressed / (1024 * 1024), 2)} MB)");
        reportContent.AppendLine($"Compression Ratio:   {ratio:F2}:1");
        reportContent.AppendLine("-------------------------------------------------");
        reportContent.AppendLine("Sample Contents:");
        foreach (var sample in entrySamples)
        {
            reportContent.AppendLine(sample);
        }
        if (entryCount > entrySamples.Count)
        {
            reportContent.AppendLine($"  ... and {entryCount - entrySamples.Count} more entries.");
        }
        reportContent.AppendLine("=================================================");

        await File.WriteAllTextAsync(reportPath, reportContent.ToString(), cancellationToken);
        metadata["Report"] = reportContent.ToString();

        return (reportPath, metadata);
    }
}
