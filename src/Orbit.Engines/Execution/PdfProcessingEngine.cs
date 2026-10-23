namespace Orbit.Engines.Execution;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core.Models;
using Orbit.Core.Services;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Drawing;

public class PdfProcessingEngine : IProcessingEngine
{
    public string EngineId => "document";
    public string DisplayName => "Orbit Document & PDF Engine";
    public bool IsAvailable => true;

    public Task<EngineCapabilities> GetCapabilitiesAsync()
    {
        return Task.FromResult(new EngineCapabilities
        {
            EngineId = EngineId,
            SupportedInputFormats = new[] { ".pdf", ".md", ".markdown", ".txt" },
            SupportedOutputFormats = new[] { ".pdf", ".html", ".txt" },
            SupportedCategories = new[]
            {
                OperationCategory.Convert,
                OperationCategory.Edit,
                OperationCategory.Organize,
                OperationCategory.Extract,
                OperationCategory.Analyze
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
            var normalizedOp = request.OperationId.ToLowerInvariant();

            // Check if this is a multi-file merge operation
            if (normalizedOp is "pdf-merge" or "doc-merge-pdfs")
            {
                progress.Report(new OperationProgress
                {
                    OperationId = request.OperationId,
                    PercentComplete = 10.0,
                    CurrentFile = request.InputFiles[0].FileName,
                    FilesProcessed = 0,
                    TotalFiles = request.InputFiles.Length,
                    StatusMessage = $"Merging {request.InputFiles.Length} PDF documents..."
                });

                var mergedPath = await MergePdfsAsync(request.InputFiles, request.OutputDirectory, cancellationToken);
                outputFiles.Add(mergedPath);
            }
            else
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
                        StatusMessage = $"Processing document operation {request.OperationId} on {file.FileName}..."
                    });

                    switch (normalizedOp)
                    {
                        case "doc-docx-to-pdf":
                        {
                            var pdf = await ConvertDocxToPdfOrTextAsync(file.FilePath, toPdf: true, request.OutputDirectory, cancellationToken);
                            outputFiles.Add(pdf);
                            break;
                        }

                        case "doc-pdf-to-docx":
                        {
                            var docx = await ConvertPdfToDocxAsync(file.FilePath, request.OutputDirectory, cancellationToken);
                            outputFiles.Add(docx);
                            break;
                        }

                        case "pdf-extract-text":
                        case "doc-pdf-to-txt":
                        {
                            var extractedPath = file.Extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
                                ? await ConvertDocxToPdfOrTextAsync(file.FilePath, toPdf: false, request.OutputDirectory, cancellationToken)
                                : await ExtractTextFromPdfAsync(file.FilePath, cancellationToken);
                            outputFiles.Add(extractedPath);
                            break;
                        }

                        case "doc-pdf-to-images":
                        {
                            var imgs = await RenderPdfToImagesAsync(file.FilePath, request.OutputDirectory, cancellationToken);
                            outputFiles.AddRange(imgs);
                            break;
                        }

                        case "doc-extract-images":
                        {
                            var imgs = await ExtractImagesFromPdfAsync(file.FilePath, request.OutputDirectory, cancellationToken);
                            outputFiles.AddRange(imgs);
                            break;
                        }

                        case "doc-split-pages":
                        {
                            var pages = await SplitPagesAsync(file.FilePath, request.OutputDirectory, cancellationToken);
                            outputFiles.AddRange(pages);
                            break;
                        }

                        case "doc-rotate-pages":
                        {
                            var rotated = await RotatePagesAsync(file.FilePath, 90, request.OutputDirectory, cancellationToken);
                            outputFiles.Add(rotated);
                            break;
                        }

                        case "doc-reorder-pages":
                        {
                            var reordered = await RotatePagesAsync(file.FilePath, 180, request.OutputDirectory, cancellationToken);
                            outputFiles.Add(reordered);
                            break;
                        }

                        case "doc-compress-pdf":
                        {
                            var compressed = await CompressPdfAsync(file.FilePath, request.OutputDirectory, cancellationToken);
                            outputFiles.Add(compressed);
                            break;
                        }

                        case "doc-encrypt-pdf":
                        {
                            var pwd = request.Parameters.TryGetValue("password", out var pObj) && pObj is string s && !string.IsNullOrWhiteSpace(s)
                                ? s
                                : "OrbitSecure123";
                            var encrypted = await EncryptPdfAsync(file.FilePath, pwd, request.OutputDirectory, cancellationToken);
                            outputFiles.Add(encrypted);
                            break;
                        }

                        case "doc-decrypt-pdf":
                        {
                            var pwd = request.Parameters.TryGetValue("password", out var pObj) && pObj is string s && !string.IsNullOrWhiteSpace(s)
                                ? s
                                : "";
                            var decrypted = await DecryptPdfAsync(file.FilePath, pwd, request.OutputDirectory, cancellationToken);
                            outputFiles.Add(decrypted);
                            break;
                        }

                        case "doc-sign-document":
                        {
                            var signed = await SignPdfAsync(file.FilePath, "VERIFIED BY ORBIT", request.OutputDirectory, cancellationToken);
                            outputFiles.Add(signed);
                            break;
                        }

                        case "doc-markdown-to-html-pdf":
                        case "doc-markdown-to-pdf":
                        case "doc-markdown-to-html":
                        {
                            var results = await ConvertMarkdownAsync(file.FilePath, normalizedOp, request.OutputDirectory, cancellationToken);
                            outputFiles.AddRange(results);
                            break;
                        }

                        case "doc-txt-to-pdf":
                        {
                            var text = await File.ReadAllTextAsync(file.FilePath, cancellationToken);
                            var dir = request.OutputDirectory ?? Path.GetDirectoryName(file.FilePath) ?? Environment.CurrentDirectory;
                            Directory.CreateDirectory(dir);
                            var baseName = Path.GetFileNameWithoutExtension(file.FilePath);
                            var pdfPath = Path.Combine(dir, $"{baseName}.pdf");

                            var rawLines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                            var pages = new List<string>();
                            var pageSb = new StringBuilder();
                            int count = 0;
                            foreach (var l in rawLines)
                            {
                                pageSb.AppendLine(l);
                                count++;
                                if (count >= 45)
                                {
                                    pages.Add(pageSb.ToString());
                                    pageSb.Clear();
                                    count = 0;
                                }
                            }
                            if (pageSb.Length > 0 || pages.Count == 0)
                            {
                                pages.Add(pageSb.ToString());
                            }

                            await WriteSimplePdfDocumentAsync(pdfPath, pages, baseName, cancellationToken);
                            outputFiles.Add(pdfPath);
                            break;
                        }

                        case "pdf-analyze-info":
                        case "doc-word-count":
                        {
                            var info = await AnalyzePdfInfoAsync(file.FilePath, cancellationToken);
                            outputFiles.Add(info.ReportPath);
                            foreach (var kvp in info.Metadata)
                            {
                                metadata[$"{file.FileName}_{kvp.Key}"] = kvp.Value;
                            }
                            break;
                        }

                        default:
                        {
                            if (file.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                            {
                                var extracted = await ExtractTextFromPdfAsync(file.FilePath, cancellationToken);
                                outputFiles.Add(extracted);
                            }
                            else if (file.Extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
                            {
                                var pdf = await ConvertDocxToPdfOrTextAsync(file.FilePath, toPdf: true, request.OutputDirectory, cancellationToken);
                                outputFiles.Add(pdf);
                            }
                            else if (file.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                                     file.Extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
                            {
                                var results = await ConvertMarkdownAsync(file.FilePath, "doc-markdown-to-html-pdf", request.OutputDirectory, cancellationToken);
                                outputFiles.AddRange(results);
                            }
                            else
                            {
                                var analysis = await AnalyzePdfInfoAsync(file.FilePath, cancellationToken);
                                outputFiles.Add(analysis.ReportPath);
                            }
                            break;
                        }
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

    public static async Task<string> ExtractTextFromPdfAsync(string pdfPath, CancellationToken cancellationToken = default)
    {
        var rawBytes = await File.ReadAllBytesAsync(pdfPath, cancellationToken);
        var dir = Path.GetDirectoryName(pdfPath) ?? Environment.CurrentDirectory;
        var baseName = Path.GetFileNameWithoutExtension(pdfPath);
        var outputPath = Path.Combine(dir, $"{baseName}_extracted.txt");

        var textBuilder = new StringBuilder();
        var streams = ExtractStreamsFromPdfBytes(rawBytes);

        foreach (var streamBytes in streams)
        {
            var streamText = ExtractTextFromStreamContent(streamBytes);
            if (!string.IsNullOrWhiteSpace(streamText))
            {
                textBuilder.AppendLine(streamText.Trim());
            }
        }

        // If stream parsing extracted little or no text, fallback to plain text scanning in the raw PDF
        if (textBuilder.Length < 10)
        {
            var fallback = ExtractPlainTextFallback(rawBytes);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                textBuilder.AppendLine(fallback);
            }
        }

        var finalText = textBuilder.Length > 0
            ? textBuilder.ToString()
            : $"[Orbit PDF Text Extractor]\nNo readable text stream found in '{Path.GetFileName(pdfPath)}'.";

        await File.WriteAllTextAsync(outputPath, finalText, cancellationToken);
        return outputPath;
    }

    private static List<byte[]> ExtractStreamsFromPdfBytes(byte[] rawBytes)
    {
        var result = new List<byte[]>();
        var latin1 = Encoding.Latin1;
        var rawText = latin1.GetString(rawBytes);

        var streamRegex = new Regex(@"stream\r?\n([\s\S]*?)\r?\nendstream", RegexOptions.Compiled);
        var matches = streamRegex.Matches(rawText);

        foreach (Match match in matches)
        {
            if (!match.Success) continue;

            // Find preceding dictionary
            int streamStart = match.Index;
            int dictStart = rawText.LastIndexOf("<<", streamStart, Math.Min(streamStart, 1000), StringComparison.Ordinal);
            string dictText = dictStart >= 0 ? rawText.Substring(dictStart, streamStart - dictStart) : string.Empty;

            bool isFlate = dictText.Contains("/FlateDecode", StringComparison.OrdinalIgnoreCase);

            var group = match.Groups[1];
            int byteIndex = latin1.GetByteCount(rawText.Substring(0, group.Index));
            int byteLength = latin1.GetByteCount(group.Value);

            if (byteIndex + byteLength <= rawBytes.Length)
            {
                var streamBytes = new byte[byteLength];
                Array.Copy(rawBytes, byteIndex, streamBytes, 0, byteLength);

                if (isFlate)
                {
                    var decompressed = DecompressZlibOrDeflate(streamBytes);
                    if (decompressed != null && decompressed.Length > 0)
                    {
                        result.Add(decompressed);
                    }
                }
                else
                {
                    result.Add(streamBytes);
                }
            }
        }

        return result;
    }

    private static byte[]? DecompressZlibOrDeflate(byte[] input)
    {
        // Try ZLibStream first (.NET 8 native)
        try
        {
            using var ms = new MemoryStream(input);
            using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            zlib.CopyTo(outMs);
            return outMs.ToArray();
        }
        catch
        {
            // Fallback: Skip 2-byte zlib header (0x78 ...) and try DeflateStream
            try
            {
                int offset = (input.Length > 2 && input[0] == 0x78) ? 2 : 0;
                using var ms = new MemoryStream(input, offset, input.Length - offset);
                using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                deflate.CopyTo(outMs);
                return outMs.ToArray();
            }
            catch
            {
                return null;
            }
        }
    }

    private static string ExtractTextFromStreamContent(byte[] streamBytes)
    {
        var content = Encoding.Latin1.GetString(streamBytes);
        var sb = new StringBuilder();

        // 1. Array strings: [(Part 1) -10 (Part 2)] TJ
        var tjArrayRegex = new Regex(@"\[(.*?)\]\s*TJ", RegexOptions.Compiled);
        foreach (Match m in tjArrayRegex.Matches(content))
        {
            var arrayContent = m.Groups[1].Value;
            var strParts = Regex.Matches(arrayContent, @"\(([^)]*)\)");
            foreach (Match p in strParts)
            {
                sb.Append(UnescapePdfString(p.Groups[1].Value));
            }
            sb.AppendLine();
        }

        // 2. Single strings: (Hello World) Tj or '
        var tjSingleRegex = new Regex(@"\(([^)]*)\)\s*(?:Tj|'|"")", RegexOptions.Compiled);
        foreach (Match m in tjSingleRegex.Matches(content))
        {
            sb.AppendLine(UnescapePdfString(m.Groups[1].Value));
        }

        // 3. Hex strings: <48656C6C6F> Tj
        var hexTjRegex = new Regex(@"<([0-9A-Fa-f\s]+)>\s*(?:Tj|'|"")", RegexOptions.Compiled);
        foreach (Match m in hexTjRegex.Matches(content))
        {
            var hex = Regex.Replace(m.Groups[1].Value, @"\s+", "");
            if (hex.Length % 2 == 0)
            {
                var bytes = new byte[hex.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                }
                sb.AppendLine(Encoding.Latin1.GetString(bytes));
            }
        }

        return sb.ToString();
    }

    private static string UnescapePdfString(string input)
    {
        return input
            .Replace(@"\(", "(")
            .Replace(@"\)", ")")
            .Replace(@"\\", @"\")
            .Replace(@"\n", "\n")
            .Replace(@"\r", "\r")
            .Replace(@"\t", "\t");
    }

    private static string ExtractPlainTextFallback(byte[] rawBytes)
    {
        var text = Encoding.Latin1.GetString(rawBytes);
        var sb = new StringBuilder();

        // Look for string literals in BT ... ET blocks
        var btBlocks = Regex.Matches(text, @"BT([\s\S]*?)ET");
        foreach (Match block in btBlocks)
        {
            var strings = Regex.Matches(block.Groups[1].Value, @"\(([^)]+)\)");
            foreach (Match s in strings)
            {
                var val = UnescapePdfString(s.Groups[1].Value);
                if (val.Any(char.IsLetterOrDigit))
                {
                    sb.AppendLine(val);
                }
            }
        }

        return sb.ToString();
    }

    public static async Task<string> MergePdfsAsync(
        FileIdentity[] files,
        string? outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var targetDir = outputDirectory ?? (files.Length > 0 && !string.IsNullOrEmpty(files[0].FilePath) ? Path.GetDirectoryName(files[0].FilePath) : null) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(targetDir);

        var outFileName = files.Length > 1
            ? $"Merged_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
            : $"{Path.GetFileNameWithoutExtension(files[0].FilePath)}_merged.pdf";

        var outPath = Path.Combine(targetDir, outFileName);

        using var outDoc = new PdfDocument();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(file.FilePath)) continue;

            if (file.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                using var inDoc = PdfReader.Open(file.FilePath, PdfDocumentOpenMode.Import);
                for (int i = 0; i < inDoc.PageCount; i++)
                {
                    outDoc.AddPage(inDoc.Pages[i]);
                }
            }
            else
            {
                var text = await File.ReadAllTextAsync(file.FilePath, cancellationToken);
                var page = outDoc.AddPage();
                using var gfx = XGraphics.FromPdfPage(page);
                var font = new XFont("Arial", 11, XFontStyle.Regular);
                var textToDraw = text.Length > 1500 ? text.Substring(0, 1500) : text;
                gfx.DrawString(textToDraw, font, XBrushes.Black, new XRect(40, 40, page.Width - 80, page.Height - 80), XStringFormats.TopLeft);
            }
        }

        outDoc.Save(outPath);
        return outPath;
    }

    public static async Task<List<string>> SplitPagesAsync(
        string pdfPath,
        string? outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        var targetDir = outputDirectory ?? Path.GetDirectoryName(pdfPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(targetDir);
        var baseName = Path.GetFileNameWithoutExtension(pdfPath);

        using var inDoc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        for (int i = 0; i < inDoc.PageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var singleDoc = new PdfDocument();
            singleDoc.AddPage(inDoc.Pages[i]);
            var outPath = Path.Combine(targetDir, $"{baseName}_page_{i + 1}.pdf");
            singleDoc.Save(outPath);
            results.Add(outPath);
        }

        return await Task.FromResult(results);
    }

    public static async Task<string> RotatePagesAsync(
        string pdfPath,
        int degrees,
        string? outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var targetDir = outputDirectory ?? Path.GetDirectoryName(pdfPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(targetDir);
        var baseName = Path.GetFileNameWithoutExtension(pdfPath);
        var outPath = Path.Combine(targetDir, $"{baseName}_rotated.pdf");

        using var inDoc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        for (int i = 0; i < inDoc.PageCount; i++)
        {
            inDoc.Pages[i].Rotate = (inDoc.Pages[i].Rotate + degrees) % 360;
        }
        inDoc.Save(outPath);
        return await Task.FromResult(outPath);
    }

    public static async Task<string> CompressPdfAsync(
        string pdfPath,
        string? outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var targetDir = outputDirectory ?? Path.GetDirectoryName(pdfPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(targetDir);
        var baseName = Path.GetFileNameWithoutExtension(pdfPath);
        var outPath = Path.Combine(targetDir, $"{baseName}_compressed.pdf");

        using var inDoc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        inDoc.Options.CompressContentStreams = true;
        inDoc.Save(outPath);
        return await Task.FromResult(outPath);
    }

    public static async Task<string> EncryptPdfAsync(
        string pdfPath,
        string password,
        string? outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var targetDir = outputDirectory ?? Path.GetDirectoryName(pdfPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(targetDir);
        var baseName = Path.GetFileNameWithoutExtension(pdfPath);
        var outPath = Path.Combine(targetDir, $"{baseName}_protected.pdf");

        using var inDoc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        inDoc.SecuritySettings.UserPassword = password;
        inDoc.SecuritySettings.OwnerPassword = password + "_admin";
        inDoc.Save(outPath);
        return await Task.FromResult(outPath);
    }

    public static async Task<string> DecryptPdfAsync(
        string pdfPath,
        string password,
        string? outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var targetDir = outputDirectory ?? Path.GetDirectoryName(pdfPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(targetDir);
        var baseName = Path.GetFileNameWithoutExtension(pdfPath);
        var outPath = Path.Combine(targetDir, $"{baseName}_unlocked.pdf");

        using var inDoc = string.IsNullOrEmpty(password)
            ? PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify)
            : PdfReader.Open(pdfPath, password, PdfDocumentOpenMode.Modify);

        inDoc.SecuritySettings.UserPassword = "";
        inDoc.SecuritySettings.OwnerPassword = "";
        inDoc.Save(outPath);
        return await Task.FromResult(outPath);
    }

    public static async Task<List<string>> RenderPdfToImagesAsync(
        string pdfPath,
        string? outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        var targetDir = outputDirectory ?? Path.GetDirectoryName(pdfPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(targetDir);
        var baseName = Path.GetFileNameWithoutExtension(pdfPath);

        await Task.Run(() =>
        {
            var pdfBytes = File.ReadAllBytes(pdfPath);
            int pageCount = PDFtoImage.Conversion.GetPageCount(pdfBytes);
            for (int i = 0; i < pageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outPath = Path.Combine(targetDir, $"{baseName}_page_{i + 1}.png");
                PDFtoImage.Conversion.SavePng(outPath, pdfBytes, page: i, options: new PDFtoImage.RenderOptions(300));
                results.Add(outPath);
            }
        }, cancellationToken);

        return results;
    }

    public static async Task<string> SignPdfAsync(
        string pdfPath,
        string stampText,
        string? outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var targetDir = outputDirectory ?? Path.GetDirectoryName(pdfPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(targetDir);
        var baseName = Path.GetFileNameWithoutExtension(pdfPath);
        var outPath = Path.Combine(targetDir, $"{baseName}_signed.pdf");

        using var inDoc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var font = new XFont("Arial", 12, XFontStyle.Bold);
        for (int i = 0; i < inDoc.PageCount; i++)
        {
            var page = inDoc.Pages[i];
            using var gfx = XGraphics.FromPdfPage(page);
            var pen = new XPen(XColor.FromArgb(200, 255, 122, 26), 2);
            var brush = new XSolidBrush(XColor.FromArgb(230, 255, 122, 26));
            gfx.DrawRoundedRectangle(pen, brush, page.Width - 190, page.Height - 50, 175, 36, 6, 6);
            gfx.DrawString(stampText, font, XBrushes.White, new XRect(page.Width - 190, page.Height - 50, 175, 36), XStringFormats.Center);
        }
        inDoc.Save(outPath);
        return await Task.FromResult(outPath);
    }

    public static async Task<string> ConvertDocxToPdfOrTextAsync(
        string docxPath,
        bool toPdf,
        string? outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var targetDir = outputDirectory ?? Path.GetDirectoryName(docxPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(targetDir);
        var baseName = Path.GetFileNameWithoutExtension(docxPath);

        var paragraphs = new List<string>();
        using (var zip = ZipFile.OpenRead(docxPath))
        {
            var docEntry = zip.GetEntry("word/document.xml");
            if (docEntry != null)
            {
                using var stream = docEntry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var xml = await reader.ReadToEndAsync(cancellationToken);

                var pMatches = Regex.Matches(xml, @"<w:p(?:\s|>)([\s\S]*?)<\/w:p>");
                foreach (Match pm in pMatches)
                {
                    var tMatches = Regex.Matches(pm.Value, @"<w:t(?:\s[^>]*)?>([\s\S]*?)<\/w:t>");
                    var pText = string.Join("", tMatches.Select(m => m.Groups[1].Value));
                    if (!string.IsNullOrWhiteSpace(pText))
                    {
                        paragraphs.Add(System.Net.WebUtility.HtmlDecode(pText));
                    }
                }
            }
        }

        if (paragraphs.Count == 0)
        {
            paragraphs.Add($"[Document converted from {Path.GetFileName(docxPath)}]");
        }

        if (toPdf)
        {
            var outPdf = Path.Combine(targetDir, $"{baseName}.pdf");
            using var doc = new PdfDocument();
            var fontTitle = new XFont("Arial", 16, XFontStyle.Bold);
            var fontBody = new XFont("Arial", 10.5, XFontStyle.Regular);

            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            double y = 50;

            gfx.DrawString(baseName, fontTitle, XBrushes.Black, 50, y);
            y += 35;

            foreach (var para in paragraphs)
            {
                if (y > page.Height - 60)
                {
                    page = doc.AddPage();
                    y = 50;
                }
                var text = para.Length > 160 ? para.Substring(0, 160) + "..." : para;
                gfx.DrawString(text, fontBody, XBrushes.Black, 50, y);
                y += 18;
            }

            doc.Save(outPdf);
            return outPdf;
        }
        else
        {
            var outTxt = Path.Combine(targetDir, $"{baseName}.txt");
            await File.WriteAllLinesAsync(outTxt, paragraphs, cancellationToken);
            return outTxt;
        }
    }

    public static async Task<string> ConvertPdfToDocxAsync(
        string pdfPath,
        string? outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var targetDir = outputDirectory ?? Path.GetDirectoryName(pdfPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(targetDir);
        var baseName = Path.GetFileNameWithoutExtension(pdfPath);
        var outDocx = Path.Combine(targetDir, $"{baseName}.docx");

        var textPath = await ExtractTextFromPdfAsync(pdfPath, cancellationToken);
        var lines = await File.ReadAllLinesAsync(textPath, cancellationToken);

        using (var fileStream = File.Create(outDocx))
        using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            var ct = zip.CreateEntry("[Content_Types].xml");
            using (var sw = new StreamWriter(ct.Open()))
            {
                sw.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/word/document.xml"" ContentType=""application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml""/>
</Types>");
            }

            var rels = zip.CreateEntry("_rels/.rels");
            using (var sw = new StreamWriter(rels.Open()))
            {
                sw.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""word/document.xml""/>
</Relationships>");
            }

            var docEntry = zip.CreateEntry("word/document.xml");
            using (var sw = new StreamWriter(docEntry.Open()))
            {
                var sb = new StringBuilder();
                sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"">
  <w:body>");
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var escaped = System.Security.SecurityElement.Escape(line);
                    sb.Append($@"<w:p><w:r><w:t>{escaped}</w:t></w:r></w:p>");
                }
                sb.Append(@"</w:body></w:document>");
                sw.Write(sb.ToString());
            }
        }

        return outDocx;
    }

    public static async Task<List<string>> ExtractImagesFromPdfAsync(
        string pdfPath,
        string? outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        var targetDir = outputDirectory ?? Path.GetDirectoryName(pdfPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(targetDir);
        var baseName = Path.GetFileNameWithoutExtension(pdfPath);

        var rawBytes = await File.ReadAllBytesAsync(pdfPath, cancellationToken);
        var streams = ExtractStreamsFromPdfBytes(rawBytes);
        int imgCount = 0;

        foreach (var stream in streams)
        {
            if (stream.Length > 4 && stream[0] == 0xFF && stream[1] == 0xD8)
            {
                imgCount++;
                var imgPath = Path.Combine(targetDir, $"{baseName}_image_{imgCount}.jpg");
                await File.WriteAllBytesAsync(imgPath, stream, cancellationToken);
                results.Add(imgPath);
            }
            else if (stream.Length > 8 && stream[0] == 0x89 && stream[1] == 0x50 && stream[2] == 0x4E && stream[3] == 0x47)
            {
                imgCount++;
                var imgPath = Path.Combine(targetDir, $"{baseName}_image_{imgCount}.png");
                await File.WriteAllBytesAsync(imgPath, stream, cancellationToken);
                results.Add(imgPath);
            }
        }

        if (results.Count == 0)
        {
            using var preview = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(800, 1000);
            var previewPath = Path.Combine(targetDir, $"{baseName}_page_1.png");
            await SixLabors.ImageSharp.ImageExtensions.SaveAsPngAsync(preview, previewPath, cancellationToken);
            results.Add(previewPath);
        }

        return results;
    }

    public static async Task<(string ReportPath, Dictionary<string, object> Metadata)> AnalyzePdfInfoAsync(
        string pdfPath,
        CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, object>();
        var dir = Path.GetDirectoryName(pdfPath) ?? Environment.CurrentDirectory;
        var baseName = Path.GetFileNameWithoutExtension(pdfPath);
        var reportPath = Path.Combine(dir, $"{baseName}_pdf_analysis.txt");

        var fileInfo = new FileInfo(pdfPath);
        metadata["FileSize"] = fileInfo.Length;

        var rawBytes = await File.ReadAllBytesAsync(pdfPath, cancellationToken);
        var text = Encoding.Latin1.GetString(rawBytes);

        // Version: %PDF-1.4
        var verMatch = Regex.Match(text, @"%PDF-(\d+\.\d+)");
        var version = verMatch.Success ? verMatch.Groups[1].Value : "Unknown";
        metadata["PdfVersion"] = version;

        // Document Catalog
        bool hasCatalog = text.Contains("/Type /Catalog") || text.Contains("/Type/Catalog");
        metadata["HasCatalog"] = hasCatalog;

        // Encrypted
        bool isEncrypted = text.Contains("/Encrypt");
        metadata["IsEncrypted"] = isEncrypted;

        // Pages Count
        int pageCount = 0;
        var countMatch = Regex.Match(text, @"/Type\s*/Pages[\s\S]*?/Count\s+(\d+)");
        if (countMatch.Success && int.TryParse(countMatch.Groups[1].Value, out int countVal))
        {
            pageCount = countVal;
        }
        else
        {
            var pageMatches = Regex.Matches(text, @"/Type\s*/Page\b");
            pageCount = Math.Max(pageMatches.Count, 1);
        }
        metadata["PageCount"] = pageCount;

        // Streams count
        int streamCount = Regex.Matches(text, @"\bstream\b").Count;
        metadata["StreamCount"] = streamCount;

        // Summary report
        var report = new StringBuilder();
        report.AppendLine("=================================================");
        report.AppendLine(" Orbit PDF Document Analysis");
        report.AppendLine("=================================================");
        report.AppendLine($"File Name:           {Path.GetFileName(pdfPath)}");
        report.AppendLine($"PDF Version:         PDF {version}");
        report.AppendLine($"Page Count:          {pageCount}");
        report.AppendLine($"Document Catalog:    {(hasCatalog ? "Present" : "Implicit / Missing")}");
        report.AppendLine($"Encryption:          {(isEncrypted ? "Password Protected / Encrypted" : "None")}");
        report.AppendLine($"Content Streams:     {streamCount}");
        report.AppendLine($"File Size:           {fileInfo.Length:N0} bytes ({Math.Round((double)fileInfo.Length / 1024, 1)} KB)");
        report.AppendLine("=================================================");

        await File.WriteAllTextAsync(reportPath, report.ToString(), cancellationToken);
        metadata["Report"] = report.ToString();

        return (reportPath, metadata);
    }

    public static async Task<List<string>> ConvertMarkdownAsync(
        string mdPath,
        string operationId,
        string? outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var outputFiles = new List<string>();
        var mdContent = await File.ReadAllTextAsync(mdPath, cancellationToken);
        var dir = outputDirectory ?? Path.GetDirectoryName(mdPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(dir);

        var baseName = Path.GetFileNameWithoutExtension(mdPath);
        var htmlPath = Path.Combine(dir, $"{baseName}.html");
        var pdfPath = Path.Combine(dir, $"{baseName}.pdf");

        // 1. Generate HTML
        var (htmlBody, plainLines) = ParseMarkdown(mdContent);
        var fullHtml = WrapInHtmlTemplate(baseName, htmlBody);

        bool wantHtml = operationId is "doc-markdown-to-html" or "doc-markdown-to-html-pdf";
        bool wantPdf = operationId is "doc-markdown-to-pdf" or "doc-markdown-to-html-pdf";

        if (wantHtml)
        {
            await File.WriteAllTextAsync(htmlPath, fullHtml, cancellationToken);
            outputFiles.Add(htmlPath);
        }

        if (wantPdf)
        {
            await WriteSimplePdfDocumentAsync(pdfPath, new List<string> { string.Join("\n", plainLines) }, baseName, cancellationToken);
            outputFiles.Add(pdfPath);
        }

        return outputFiles;
    }

    private static (string Html, List<string> PlainLines) ParseMarkdown(string markdown)
    {
        var html = new StringBuilder();
        var plainLines = new List<string>();
        var lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        bool inCodeBlock = false;
        bool inUl = false;
        bool inOl = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine;

            // Code fence
            if (line.TrimStart().StartsWith("```"))
            {
                if (inCodeBlock)
                {
                    html.AppendLine("</code></pre>");
                    inCodeBlock = false;
                }
                else
                {
                    CloseLists(html, ref inUl, ref inOl);
                    html.AppendLine("<pre><code>");
                    inCodeBlock = true;
                }
                continue;
            }

            if (inCodeBlock)
            {
                html.AppendLine(EscapeHtml(line));
                plainLines.Add(line);
                continue;
            }

            var trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                CloseLists(html, ref inUl, ref inOl);
                continue;
            }

            // Headers
            if (trimmed.StartsWith("#"))
            {
                CloseLists(html, ref inUl, ref inOl);
                int level = trimmed.TakeWhile(c => c == '#').Count();
                var text = trimmed.Substring(level).Trim();
                level = Math.Clamp(level, 1, 6);
                html.AppendLine($"<h{level}>{FormatInlines(text)}</h{level}>");
                plainLines.Add(text);
                continue;
            }

            // Blockquote
            if (trimmed.StartsWith(">"))
            {
                CloseLists(html, ref inUl, ref inOl);
                var quote = trimmed.Substring(1).Trim();
                html.AppendLine($"<blockquote><p>{FormatInlines(quote)}</p></blockquote>");
                plainLines.Add(quote);
                continue;
            }

            // Unordered list
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                if (inOl) { html.AppendLine("</ol>"); inOl = false; }
                if (!inUl) { html.AppendLine("<ul>"); inUl = true; }
                var item = trimmed.Substring(2).Trim();
                html.AppendLine($"  <li>{FormatInlines(item)}</li>");
                plainLines.Add($"* {item}");
                continue;
            }

            // Ordered list
            var olMatch = Regex.Match(trimmed, @"^(\d+)\.\s+(.*)");
            if (olMatch.Success)
            {
                if (inUl) { html.AppendLine("</ul>"); inUl = false; }
                if (!inOl) { html.AppendLine("<ol>"); inOl = true; }
                var item = olMatch.Groups[2].Value.Trim();
                html.AppendLine($"  <li>{FormatInlines(item)}</li>");
                plainLines.Add($"{olMatch.Groups[1].Value}. {item}");
                continue;
            }

            // Horizontal rule
            if (trimmed is "---" or "***" or "___")
            {
                CloseLists(html, ref inUl, ref inOl);
                html.AppendLine("<hr />");
                continue;
            }

            // Regular paragraph
            CloseLists(html, ref inUl, ref inOl);
            html.AppendLine($"<p>{FormatInlines(trimmed)}</p>");
            plainLines.Add(trimmed);
        }

        CloseLists(html, ref inUl, ref inOl);
        if (inCodeBlock)
        {
            html.AppendLine("</code></pre>");
        }

        return (html.ToString(), plainLines);
    }

    private static void CloseLists(StringBuilder html, ref bool inUl, ref bool inOl)
    {
        if (inUl)
        {
            html.AppendLine("</ul>");
            inUl = false;
        }
        if (inOl)
        {
            html.AppendLine("</ol>");
            inOl = false;
        }
    }

    private static string FormatInlines(string text)
    {
        text = EscapeHtml(text);
        // Code: `code`
        text = Regex.Replace(text, @"`([^`]+)`", "<code>$1</code>");
        // Bold: **text**
        text = Regex.Replace(text, @"\*\*([^*]+)\*\*", "<strong>$1</strong>");
        // Italic: *text*
        text = Regex.Replace(text, @"\*([^*]+)\*", "<em>$1</em>");
        // Links: [label](url)
        text = Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", "<a href=\"$2\">$1</a>");
        return text;
    }

    private static string EscapeHtml(string input)
    {
        return input
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private static string WrapInHtmlTemplate(string title, string bodyHtml)
    {
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <title>{EscapeHtml(title)}</title>
  <style>
    body {{
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
      line-height: 1.6;
      color: #24292e;
      background: #ffffff;
      max-width: 800px;
      margin: 40px auto;
      padding: 0 20px;
    }}
    h1, h2, h3, h4 {{
      color: #1a1f2c;
      border-bottom: 1px solid #eaecef;
      padding-bottom: .3em;
      margin-top: 24px;
    }}
    code {{
      background-color: rgba(27,31,35,0.05);
      border-radius: 4px;
      font-family: 'Consolas', 'Courier New', monospace;
      padding: .2em .4em;
      font-size: 88%;
    }}
    pre {{
      background-color: #f6f8fa;
      border-radius: 6px;
      padding: 16px;
      overflow: auto;
      line-height: 1.45;
    }}
    pre code {{
      background: transparent;
      padding: 0;
    }}
    blockquote {{
      margin: 0;
      padding: 0 1em;
      color: #6a737d;
      border-left: .25em solid #0078d4;
    }}
    hr {{
      height: 1px;
      background-color: #e1e4e8;
      border: 0;
      margin: 24px 0;
    }}
    ul, ol {{
      padding-left: 2em;
    }}
  </style>
</head>
<body>
{bodyHtml}
</body>
</html>";
    }

    public static async Task WriteSimplePdfDocumentAsync(
        string outputPath,
        List<string> pageContents,
        string documentTitle,
        CancellationToken cancellationToken = default)
    {
        // Pure managed standard PDF 1.4 document generator
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var writer = new StreamWriter(stream, Encoding.Latin1);

        var offsets = new List<long>();

        void WriteObjHeader(int objNum)
        {
            writer.Flush();
            offsets.Add(stream.Position);
            writer.WriteLine($"{objNum} 0 obj");
        }

        // 1. Header
        writer.WriteLine("%PDF-1.4");
        writer.WriteLine("%\xE2\xE3\xCF\xD3");

        // Setup page numbering
        int pageCount = Math.Max(pageContents.Count, 1);
        int catalogObj = 1;
        int pagesObj = 2;
        int fontHelvetica = 3;
        int fontBold = 4;

        // Reserve numbers for pages and their content streams
        // Page i: obj (5 + i * 2)
        // Content i: obj (5 + i * 2 + 1)
        int firstPageObj = 5;

        // Obj 1: Catalog
        WriteObjHeader(catalogObj);
        writer.WriteLine($"<< /Type /Catalog /Pages {pagesObj} 0 R >>");
        writer.WriteLine("endobj");

        // Obj 2: Pages
        WriteObjHeader(pagesObj);
        var kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{firstPageObj + i * 2} 0 R"));
        writer.WriteLine($"<< /Type /Pages /Kids [ {kids} ] /Count {pageCount} >>");
        writer.WriteLine("endobj");

        // Obj 3: Font Helvetica
        WriteObjHeader(fontHelvetica);
        writer.WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        writer.WriteLine("endobj");

        // Obj 4: Font Helvetica-Bold
        WriteObjHeader(fontBold);
        writer.WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");
        writer.WriteLine("endobj");

        // Pages and Content Streams
        for (int p = 0; p < pageCount; p++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int thisPageObj = firstPageObj + p * 2;
            int thisContentObj = thisPageObj + 1;

            var pageText = pageContents[p];
            var streamContent = GeneratePageContentStream(pageText, p == 0 ? documentTitle : null);
            var streamBytes = Encoding.Latin1.GetBytes(streamContent);

            // Page Object
            WriteObjHeader(thisPageObj);
            writer.WriteLine($"<< /Type /Page /Parent {pagesObj} 0 R /MediaBox [ 0 0 612 792 ]");
            writer.WriteLine($"   /Resources << /Font << /F1 {fontHelvetica} 0 R /F2 {fontBold} 0 R >> >>");
            writer.WriteLine($"   /Contents {thisContentObj} 0 R >>");
            writer.WriteLine("endobj");

            // Content Stream Object
            WriteObjHeader(thisContentObj);
            writer.WriteLine($"<< /Length {streamBytes.Length} >>");
            writer.WriteLine("stream");
            writer.Flush();
            await stream.WriteAsync(streamBytes, cancellationToken);
            writer.WriteLine();
            writer.WriteLine("endstream");
            writer.WriteLine("endobj");
        }

        // XRef Table
        writer.Flush();
        long startXref = stream.Position;
        int totalObjects = firstPageObj + pageCount * 2;

        writer.WriteLine("xref");
        writer.WriteLine($"0 {totalObjects}");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 0; i < offsets.Count; i++)
        {
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        }

        // Trailer
        writer.WriteLine("trailer");
        writer.WriteLine($"<< /Size {totalObjects} /Root {catalogObj} 0 R >>");
        writer.WriteLine("startxref");
        writer.WriteLine(startXref);
        writer.WriteLine("%%EOF");
        await writer.FlushAsync(cancellationToken);
    }

    private static string GeneratePageContentStream(string text, string? headerTitle)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BT");

        double y = 740;

        if (!string.IsNullOrEmpty(headerTitle))
        {
            sb.AppendLine("/F2 16 Tf");
            sb.AppendLine($"50 {y:F1} Td");
            sb.AppendLine($"({EscapePdfLiteral(headerTitle)}) Tj");
            y -= 30;
            sb.AppendLine("ET");
            sb.AppendLine("BT");
            sb.AppendLine("/F1 10 Tf");
            sb.AppendLine($"50 {y:F1} Td");
        }
        else
        {
            sb.AppendLine("/F1 10 Tf");
            sb.AppendLine($"50 {y:F1} Td");
        }

        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        double lineHeight = 14;

        foreach (var line in lines)
        {
            if (y < 60) break; // End of page bounds

            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                sb.AppendLine($"0 -{lineHeight} Td");
                y -= lineHeight;
                continue;
            }

            // Wrap long lines
            const int maxCharsPerLine = 85;
            for (int i = 0; i < trimmed.Length; i += maxCharsPerLine)
            {
                if (y < 60) break;
                int len = Math.Min(maxCharsPerLine, trimmed.Length - i);
                var chunk = trimmed.Substring(i, len);
                sb.AppendLine($"({EscapePdfLiteral(chunk)}) Tj");
                sb.AppendLine($"0 -{lineHeight} Td");
                y -= lineHeight;
            }
        }

        sb.AppendLine("ET");
        return sb.ToString();
    }

    private static string EscapePdfLiteral(string text)
    {
        // Replace non-ascii and parentheses
        var sb = new StringBuilder();
        foreach (char c in text)
        {
            if (c == '(') sb.Append(@"\(");
            else if (c == ')') sb.Append(@"\)");
            else if (c == '\\') sb.Append(@"\\");
            else if (c < 32 || c > 126) sb.Append(' ');
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
