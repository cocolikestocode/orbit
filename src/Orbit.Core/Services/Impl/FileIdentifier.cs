namespace Orbit.Core.Services.Impl;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Orbit.Core.Models;

public class FileIdentifier : IFileIdentifier
{
    private static readonly Dictionary<string, FileFamily> ExtensionFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        // Image
        { ".png", FileFamily.Image },
        { ".jpg", FileFamily.Image },
        { ".jpeg", FileFamily.Image },
        { ".jpe", FileFamily.Image },
        { ".jfif", FileFamily.Image },
        { ".webp", FileFamily.Image },
        { ".gif", FileFamily.Image },
        { ".bmp", FileFamily.Image },
        { ".dib", FileFamily.Image },
        { ".tiff", FileFamily.Image },
        { ".tif", FileFamily.Image },
        { ".heic", FileFamily.Image },
        { ".heif", FileFamily.Image },
        { ".avif", FileFamily.Image },
        { ".svg", FileFamily.Image },
        { ".svgz", FileFamily.Image },
        { ".ico", FileFamily.Image },
        { ".raw", FileFamily.Image },
        { ".cr2", FileFamily.Image },
        { ".nef", FileFamily.Image },
        { ".arw", FileFamily.Image },
        { ".dng", FileFamily.Image },
        { ".psd", FileFamily.Image },

        // Document
        { ".pdf", FileFamily.Document },
        { ".doc", FileFamily.Document },
        { ".docx", FileFamily.Document },
        { ".docm", FileFamily.Document },
        { ".dot", FileFamily.Document },
        { ".dotx", FileFamily.Document },
        { ".rtf", FileFamily.Document },
        { ".odt", FileFamily.Document },
        { ".ott", FileFamily.Document },
        { ".pages", FileFamily.Document },
        { ".oxps", FileFamily.Document },
        { ".xps", FileFamily.Document },
        { ".tex", FileFamily.Document },
        { ".latex", FileFamily.Document },
        { ".wpd", FileFamily.Document },

        // Video
        { ".mp4", FileFamily.Video },
        { ".m4v", FileFamily.Video },
        { ".mkv", FileFamily.Video },
        { ".avi", FileFamily.Video },
        { ".mov", FileFamily.Video },
        { ".wmv", FileFamily.Video },
        { ".flv", FileFamily.Video },
        { ".webm", FileFamily.Video },
        { ".vob", FileFamily.Video },
        { ".ogv", FileFamily.Video },
        { ".mts", FileFamily.Video },
        { ".m2ts", FileFamily.Video },
        { ".3gp", FileFamily.Video },
        { ".3g2", FileFamily.Video },

        // Audio
        { ".mp3", FileFamily.Audio },
        { ".wav", FileFamily.Audio },
        { ".flac", FileFamily.Audio },
        { ".aac", FileFamily.Audio },
        { ".ogg", FileFamily.Audio },
        { ".oga", FileFamily.Audio },
        { ".m4a", FileFamily.Audio },
        { ".wma", FileFamily.Audio },
        { ".alac", FileFamily.Audio },
        { ".aiff", FileFamily.Audio },
        { ".aif", FileFamily.Audio },
        { ".opus", FileFamily.Audio },
        { ".mid", FileFamily.Audio },
        { ".midi", FileFamily.Audio },

        // Archive
        { ".zip", FileFamily.Archive },
        { ".7z", FileFamily.Archive },
        { ".rar", FileFamily.Archive },
        { ".tar", FileFamily.Archive },
        { ".gz", FileFamily.Archive },
        { ".gzip", FileFamily.Archive },
        { ".bz2", FileFamily.Archive },
        { ".bzip2", FileFamily.Archive },
        { ".xz", FileFamily.Archive },
        { ".tgz", FileFamily.Archive },
        { ".lz", FileFamily.Archive },
        { ".lzma", FileFamily.Archive },
        { ".cab", FileFamily.Archive },
        { ".ar", FileFamily.Archive },

        // Spreadsheet
        { ".xls", FileFamily.Spreadsheet },
        { ".xlsx", FileFamily.Spreadsheet },
        { ".xlsm", FileFamily.Spreadsheet },
        { ".xlsb", FileFamily.Spreadsheet },
        { ".xlt", FileFamily.Spreadsheet },
        { ".xltx", FileFamily.Spreadsheet },
        { ".ods", FileFamily.Spreadsheet },
        { ".ots", FileFamily.Spreadsheet },
        { ".numbers", FileFamily.Spreadsheet },
        { ".csv", FileFamily.Spreadsheet },
        { ".tsv", FileFamily.Spreadsheet },

        // Presentation
        { ".ppt", FileFamily.Presentation },
        { ".pptx", FileFamily.Presentation },
        { ".pptm", FileFamily.Presentation },
        { ".pot", FileFamily.Presentation },
        { ".potx", FileFamily.Presentation },
        { ".pps", FileFamily.Presentation },
        { ".ppsx", FileFamily.Presentation },
        { ".odp", FileFamily.Presentation },
        { ".otp", FileFamily.Presentation },
        { ".key", FileFamily.Presentation },

        // Code
        { ".cs", FileFamily.Code },
        { ".csx", FileFamily.Code },
        { ".cpp", FileFamily.Code },
        { ".c", FileFamily.Code },
        { ".h", FileFamily.Code },
        { ".hpp", FileFamily.Code },
        { ".cc", FileFamily.Code },
        { ".cxx", FileFamily.Code },
        { ".java", FileFamily.Code },
        { ".py", FileFamily.Code },
        { ".pyw", FileFamily.Code },
        { ".js", FileFamily.Code },
        { ".mjs", FileFamily.Code },
        { ".cjs", FileFamily.Code },
        { ".ts", FileFamily.Code },
        { ".tsx", FileFamily.Code },
        { ".jsx", FileFamily.Code },
        { ".go", FileFamily.Code },
        { ".rs", FileFamily.Code },
        { ".rb", FileFamily.Code },
        { ".php", FileFamily.Code },
        { ".swift", FileFamily.Code },
        { ".kt", FileFamily.Code },
        { ".kts", FileFamily.Code },
        { ".scala", FileFamily.Code },
        { ".sh", FileFamily.Code },
        { ".bash", FileFamily.Code },
        { ".ps1", FileFamily.Code },
        { ".psm1", FileFamily.Code },
        { ".bat", FileFamily.Code },
        { ".cmd", FileFamily.Code },
        { ".r", FileFamily.Code },
        { ".lua", FileFamily.Code },
        { ".pl", FileFamily.Code },
        { ".sql", FileFamily.Code },
        { ".dart", FileFamily.Code },
        { ".asm", FileFamily.Code },
        { ".vb", FileFamily.Code },
        { ".fs", FileFamily.Code },
        { ".fsx", FileFamily.Code },
        { ".zig", FileFamily.Code },
        { ".vue", FileFamily.Code },
        { ".svelte", FileFamily.Code },

        // Text
        { ".txt", FileFamily.Text },
        { ".text", FileFamily.Text },
        { ".md", FileFamily.Text },
        { ".markdown", FileFamily.Text },
        { ".rst", FileFamily.Text },
        { ".asc", FileFamily.Text },
        { ".asciidoc", FileFamily.Text },
        { ".nfo", FileFamily.Text },
        { ".rtx", FileFamily.Text },

        // Data
        { ".json", FileFamily.Data },
        { ".json5", FileFamily.Data },
        { ".xml", FileFamily.Data },
        { ".yaml", FileFamily.Data },
        { ".yml", FileFamily.Data },
        { ".toml", FileFamily.Data },
        { ".ini", FileFamily.Data },
        { ".cfg", FileFamily.Data },
        { ".conf", FileFamily.Data },
        { ".properties", FileFamily.Data },
        { ".proto", FileFamily.Data },
        { ".graphql", FileFamily.Data },
        { ".gql", FileFamily.Data },
        { ".ndjson", FileFamily.Data },
        { ".jsonl", FileFamily.Data },

        // Font
        { ".ttf", FileFamily.Font },
        { ".otf", FileFamily.Font },
        { ".woff", FileFamily.Font },
        { ".woff2", FileFamily.Font },
        { ".eot", FileFamily.Font },
        { ".fon", FileFamily.Font },
        { ".pfb", FileFamily.Font },
        { ".pfm", FileFamily.Font },

        // Design
        { ".ai", FileFamily.Design },
        { ".sketch", FileFamily.Design },
        { ".fig", FileFamily.Design },
        { ".xd", FileFamily.Design },
        { ".blend", FileFamily.Design },
        { ".dwg", FileFamily.Design },
        { ".dxf", FileFamily.Design },
        { ".cdr", FileFamily.Design },
        { ".eps", FileFamily.Design },

        // ThreeDimensional
        { ".obj", FileFamily.ThreeDimensional },
        { ".stl", FileFamily.ThreeDimensional },
        { ".fbx", FileFamily.ThreeDimensional },
        { ".gltf", FileFamily.ThreeDimensional },
        { ".glb", FileFamily.ThreeDimensional },
        { ".dae", FileFamily.ThreeDimensional },
        { ".3ds", FileFamily.ThreeDimensional },
        { ".ply", FileFamily.ThreeDimensional },
        { ".step", FileFamily.ThreeDimensional },
        { ".stp", FileFamily.ThreeDimensional },
        { ".iges", FileFamily.ThreeDimensional },
        { ".igs", FileFamily.ThreeDimensional },

        // Scientific
        { ".fits", FileFamily.Scientific },
        { ".hdf", FileFamily.Scientific },
        { ".hdf5", FileFamily.Scientific },
        { ".h5", FileFamily.Scientific },
        { ".nc", FileFamily.Scientific },
        { ".netcdf", FileFamily.Scientific },
        { ".mat", FileFamily.Scientific },
        { ".rdata", FileFamily.Scientific },
        { ".rds", FileFamily.Scientific },
        { ".pdb", FileFamily.Scientific },
        { ".cif", FileFamily.Scientific },

        // Ebook
        { ".epub", FileFamily.Ebook },
        { ".mobi", FileFamily.Ebook },
        { ".azw", FileFamily.Ebook },
        { ".azw3", FileFamily.Ebook },
        { ".fb2", FileFamily.Ebook },
        { ".ibooks", FileFamily.Ebook },
        { ".djvu", FileFamily.Ebook },
        { ".cbr", FileFamily.Ebook },
        { ".cbz", FileFamily.Ebook },

        // Email
        { ".eml", FileFamily.Email },
        { ".msg", FileFamily.Email },
        { ".mbox", FileFamily.Email },
        { ".pst", FileFamily.Email },
        { ".ost", FileFamily.Email },
        { ".vcf", FileFamily.Email },
        { ".vcard", FileFamily.Email },

        // Certificate
        { ".cer", FileFamily.Certificate },
        { ".crt", FileFamily.Certificate },
        { ".pem", FileFamily.Certificate },
        { ".der", FileFamily.Certificate },
        { ".pfx", FileFamily.Certificate },
        { ".p12", FileFamily.Certificate },
        { ".csr", FileFamily.Certificate },
        { ".crl", FileFamily.Certificate },

        // Executable
        { ".exe", FileFamily.Executable },
        { ".dll", FileFamily.Executable },
        { ".so", FileFamily.Executable },
        { ".dylib", FileFamily.Executable },
        { ".bin", FileFamily.Executable },
        { ".msi", FileFamily.Executable },
        { ".com", FileFamily.Executable },
        { ".apk", FileFamily.Executable },
        { ".appimage", FileFamily.Executable },
        { ".deb", FileFamily.Executable },
        { ".rpm", FileFamily.Executable },

        // DiskImage
        { ".iso", FileFamily.DiskImage },
        { ".img", FileFamily.DiskImage },
        { ".vmdk", FileFamily.DiskImage },
        { ".vdi", FileFamily.DiskImage },
        { ".vhd", FileFamily.DiskImage },
        { ".vhdx", FileFamily.DiskImage },
        { ".dmg", FileFamily.DiskImage },
        { ".qcow2", FileFamily.DiskImage },

        // Database
        { ".db", FileFamily.Database },
        { ".sqlite", FileFamily.Database },
        { ".sqlite3", FileFamily.Database },
        { ".mdb", FileFamily.Database },
        { ".accdb", FileFamily.Database },
        { ".frm", FileFamily.Database },
        { ".ibd", FileFamily.Database },
        { ".mdf", FileFamily.Database },
        { ".ldf", FileFamily.Database },
        { ".bak", FileFamily.Database },

        // Log
        { ".log", FileFamily.Log },
        { ".out", FileFamily.Log },
        { ".err", FileFamily.Log },
        { ".trace", FileFamily.Log },
        { ".eventlog", FileFamily.Log }
    };

    private static readonly Dictionary<string, string> ExtensionMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".png", "image/png" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".jfif", "image/jpeg" },
        { ".webp", "image/webp" },
        { ".gif", "image/gif" },
        { ".bmp", "image/bmp" },
        { ".tiff", "image/tiff" },
        { ".tif", "image/tiff" },
        { ".svg", "image/svg+xml" },
        { ".ico", "image/x-icon" },
        { ".heic", "image/heic" },
        { ".heif", "image/heif" },
        { ".avif", "image/avif" },
        { ".pdf", "application/pdf" },
        { ".doc", "application/msword" },
        { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { ".xls", "application/vnd.ms-excel" },
        { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { ".ppt", "application/vnd.ms-powerpoint" },
        { ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
        { ".csv", "text/csv" },
        { ".tsv", "text/tab-separated-values" },
        { ".txt", "text/plain" },
        { ".md", "text/markdown" },
        { ".json", "application/json" },
        { ".xml", "application/xml" },
        { ".yaml", "application/yaml" },
        { ".yml", "application/yaml" },
        { ".html", "text/html" },
        { ".htm", "text/html" },
        { ".css", "text/css" },
        { ".js", "application/javascript" },
        { ".ts", "application/typescript" },
        { ".zip", "application/zip" },
        { ".7z", "application/x-7z-compressed" },
        { ".tar", "application/x-tar" },
        { ".gz", "application/gzip" },
        { ".rar", "application/vnd.rar" },
        { ".mp4", "video/mp4" },
        { ".mkv", "video/x-matroska" },
        { ".avi", "video/x-msvideo" },
        { ".mov", "video/quicktime" },
        { ".webm", "video/webm" },
        { ".mp3", "audio/mpeg" },
        { ".wav", "audio/wav" },
        { ".flac", "audio/flac" },
        { ".ogg", "audio/ogg" },
        { ".m4a", "audio/mp4" },
        { ".epub", "application/epub+zip" },
        { ".sqlite", "application/vnd.sqlite3" },
        { ".iso", "application/x-iso9660-image" },
        { ".exe", "application/vnd.microsoft.portable-executable" }
    };

    public FileIdentity Identify(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        // Normalize path
        var normalizedPath = Path.GetFullPath(filePath);

        if (Directory.Exists(normalizedPath))
        {
            var dirInfo = new DirectoryInfo(normalizedPath);
            return new FileIdentity
            {
                FilePath = normalizedPath,
                FileName = dirInfo.Name,
                Extension = string.Empty,
                MimeType = "inode/directory",
                Family = FileFamily.Folder,
                FileSize = 0,
                IsReadable = true,
                HasTransparency = false,
                Metadata = new Dictionary<string, object>
                {
                    ["IsDirectory"] = true,
                    ["Created"] = dirInfo.CreationTimeUtc,
                    ["Modified"] = dirInfo.LastWriteTimeUtc
                }
            };
        }

        var fileName = Path.GetFileName(normalizedPath);
        var extension = Path.GetExtension(normalizedPath).ToLowerInvariant();
        long fileSize = 0;
        var isReadable = false;
        var metadata = new Dictionary<string, object>();
        string? magicMime = null;
        FileFamily? magicFamily = null;

        if (File.Exists(normalizedPath))
        {
            try
            {
                var fileInfo = new FileInfo(normalizedPath);
                fileSize = fileInfo.Length;
                metadata["Created"] = fileInfo.CreationTimeUtc;
                metadata["Modified"] = fileInfo.LastWriteTimeUtc;

                // Magic byte probe
                var probeResult = ProbeMagicBytes(normalizedPath);
                if (probeResult != null)
                {
                    magicMime = probeResult.Value.MimeType;
                    magicFamily = probeResult.Value.Family;
                    metadata["MagicFormat"] = probeResult.Value.Format;
                }
                isReadable = true;
            }
            catch (Exception ex)
            {
                metadata["ReadError"] = ex.Message;
                isReadable = false;
            }
        }

        var family = magicFamily ?? DetectFamily(extension);
        var mimeType = magicMime ?? DetectMimeType(normalizedPath) ?? "application/octet-stream";
        var hasTransparency = DetectTransparency(extension, family);

        return new FileIdentity
        {
            FilePath = normalizedPath,
            FileName = fileName,
            Extension = extension,
            MimeType = mimeType,
            Family = family,
            FileSize = fileSize,
            IsReadable = isReadable,
            HasTransparency = hasTransparency,
            Metadata = metadata
        };
    }

    public FileIdentity[] IdentifyMultiple(string[] filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var results = new FileIdentity[filePaths.Length];
        for (var i = 0; i < filePaths.Length; i++)
        {
            results[i] = Identify(filePaths[i]);
        }
        return results;
    }

    public FileFamily DetectFamily(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return FileFamily.Unknown;
        }

        var normalizedExt = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
        return ExtensionFamilies.TryGetValue(normalizedExt, out var family) ? family : FileFamily.Unknown;
    }

    public string? DetectMimeType(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        // If file exists, try magic bytes first
        if (File.Exists(filePath))
        {
            var magic = ProbeMagicBytes(filePath);
            if (magic != null)
            {
                return magic.Value.MimeType;
            }
        }

        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
        {
            return null;
        }

        var normalizedExt = ext.StartsWith('.') ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant();
        return ExtensionMimeTypes.TryGetValue(normalizedExt, out var mime) ? mime : null;
    }

    private static bool DetectTransparency(string extension, FileFamily family)
    {
        if (family != FileFamily.Image) return false;
        return extension is ".png" or ".webp" or ".gif" or ".svg" or ".ico" or ".tiff" or ".tif" or ".avif";
    }

    private static (string Format, string MimeType, FileFamily Family)? ProbeMagicBytes(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[32];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead < 4) return null;

            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (bytesRead >= 8 &&
                buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47 &&
                buffer[4] == 0x0D && buffer[5] == 0x0A && buffer[6] == 0x1A && buffer[7] == 0x0A)
            {
                return ("PNG", "image/png", FileFamily.Image);
            }

            // JPEG: FF D8 FF
            if (buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
            {
                return ("JPEG", "image/jpeg", FileFamily.Image);
            }

            // GIF: GIF87a or GIF89a
            if (bytesRead >= 6 &&
                buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x38 &&
                (buffer[4] == 0x37 || buffer[4] == 0x39) && buffer[5] == 0x61)
            {
                return ("GIF", "image/gif", FileFamily.Image);
            }

            // BMP: 42 4D
            if (buffer[0] == 0x42 && buffer[1] == 0x4D)
            {
                return ("BMP", "image/bmp", FileFamily.Image);
            }

            // TIFF: II* (49 49 2A 00) or MM* (4D 4D 00 2A)
            if ((buffer[0] == 0x49 && buffer[1] == 0x49 && buffer[2] == 0x2A && buffer[3] == 0x00) ||
                (buffer[0] == 0x4D && buffer[1] == 0x4D && buffer[2] == 0x00 && buffer[3] == 0x2A))
            {
                return ("TIFF", "image/tiff", FileFamily.Image);
            }

            // PDF: %PDF- (25 50 44 46)
            if (buffer[0] == 0x25 && buffer[1] == 0x50 && buffer[2] == 0x44 && buffer[3] == 0x46)
            {
                return ("PDF", "application/pdf", FileFamily.Document);
            }

            // WebP: RIFF....WEBP (52 49 46 46 ... 57 45 42 50)
            if (bytesRead >= 12 &&
                buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46 &&
                buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50)
            {
                return ("WebP", "image/webp", FileFamily.Image);
            }

            // 7z: 37 7A BC AF 27 1C
            if (bytesRead >= 6 &&
                buffer[0] == 0x37 && buffer[1] == 0x7A && buffer[2] == 0xBC && buffer[3] == 0xAF &&
                buffer[4] == 0x27 && buffer[5] == 0x1C)
            {
                return ("7-Zip", "application/x-7z-compressed", FileFamily.Archive);
            }

            // RAR: 52 61 72 21 1A 07
            if (bytesRead >= 6 &&
                buffer[0] == 0x52 && buffer[1] == 0x61 && buffer[2] == 0x72 && buffer[3] == 0x21 &&
                buffer[4] == 0x1A && buffer[5] == 0x07)
            {
                return ("RAR", "application/vnd.rar", FileFamily.Archive);
            }

            // GZIP: 1F 8B
            if (buffer[0] == 0x1F && buffer[1] == 0x8B)
            {
                return ("GZIP", "application/gzip", FileFamily.Archive);
            }

            // ZIP / Office OpenXML: PK.. (50 4B 03 04 or 50 4B 05 06 or 50 4B 07 08)
            if (buffer[0] == 0x50 && buffer[1] == 0x4B &&
                (buffer[2] == 0x03 || buffer[2] == 0x05 || buffer[2] == 0x07))
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                return ext switch
                {
                    ".docx" or ".docm" => ("DOCX", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", FileFamily.Document),
                    ".xlsx" or ".xlsm" => ("XLSX", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", FileFamily.Spreadsheet),
                    ".pptx" or ".pptm" => ("PPTX", "application/vnd.openxmlformats-officedocument.presentationml.presentation", FileFamily.Presentation),
                    ".epub" => ("EPUB", "application/epub+zip", FileFamily.Ebook),
                    _ => ("ZIP", "application/zip", FileFamily.Archive)
                };
            }

            // MP4: ftyp at offset 4
            if (bytesRead >= 8 &&
                buffer[4] == 0x66 && buffer[5] == 0x74 && buffer[6] == 0x79 && buffer[7] == 0x70)
            {
                return ("MP4", "video/mp4", FileFamily.Video);
            }

            // MP3: ID3 header
            if (buffer[0] == 0x49 && buffer[1] == 0x44 && buffer[2] == 0x33)
            {
                return ("MP3", "audio/mpeg", FileFamily.Audio);
            }

            // FLAC: fLaC
            if (buffer[0] == 0x66 && buffer[1] == 0x4C && buffer[2] == 0x61 && buffer[3] == 0x43)
            {
                return ("FLAC", "audio/flac", FileFamily.Audio);
            }

            // Ogg: OggS
            if (buffer[0] == 0x4F && buffer[1] == 0x67 && buffer[2] == 0x67 && buffer[3] == 0x53)
            {
                return ("OGG", "audio/ogg", FileFamily.Audio);
            }

            // Windows Executable: MZ (4D 5A)
            if (buffer[0] == 0x4D && buffer[1] == 0x5A)
            {
                return ("Executable", "application/vnd.microsoft.portable-executable", FileFamily.Executable);
            }
        }
        catch
        {
            // Ignore file read exceptions during probe
        }

        return null;
    }
}
