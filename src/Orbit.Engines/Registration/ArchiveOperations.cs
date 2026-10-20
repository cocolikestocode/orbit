namespace Orbit.Engines.Registration;

using Orbit.Core.Models;

public static class ArchiveOperations
{
    private static readonly string[] ArchiveFormats = [".zip", ".7z", ".rar", ".tar", ".gz", ".gzip", ".bz2", ".bzip2", ".xz", ".tgz", ".lz", ".cab", ".iso"];

    public static IReadOnlyList<OperationDefinition> GetAll()
    {
        return new List<OperationDefinition>
        {
            // === Extract ===
            new()
            {
                Id = "archive-extract-all",
                Label = "Extract All Files",
                Description = "Unpack entire archive contents to a chosen destination directory.",
                Category = OperationCategory.Extract,
                InputFormats = ArchiveFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "archive",
                IconGlyph = "\uE8B7",
                KeyboardShortcut = "Ctrl+Shift+E",
                Priority = 10
            },
            new()
            {
                Id = "archive-extract-to-folder",
                Label = "Extract to Subfolder",
                Description = "Extract archive contents into a new dedicated folder named after the archive.",
                Category = OperationCategory.Extract,
                InputFormats = ArchiveFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "archive",
                IconGlyph = "\uE838",
                Priority = 11
            },

            // === Create ===
            new()
            {
                Id = "archive-create-zip",
                Label = "Compress to ZIP Archive",
                Description = "Package selected files and directories into a standard portable ZIP archive.",
                Category = OperationCategory.Create,
                InputFormats = ["*"],
                OutputFormats = [".zip"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "archive",
                IconGlyph = "\uF012",
                KeyboardShortcut = "Ctrl+Shift+Z",
                Priority = 20
            },
            new()
            {
                Id = "archive-create-7z",
                Label = "Compress to 7-Zip (High Compression)",
                Description = "Package selected items with LZMA2 algorithm for maximum compression ratio.",
                Category = OperationCategory.Create,
                InputFormats = ["*"],
                OutputFormats = [".7z"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "archive",
                IconGlyph = "\uF012",
                Priority = 21
            },
            new()
            {
                Id = "archive-create-tar-gz",
                Label = "Compress to TAR.GZ (Linux / Unix)",
                Description = "Create a POSIX tarball compressed with Gzip.",
                Category = OperationCategory.Create,
                InputFormats = ["*"],
                OutputFormats = [".tar.gz"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "archive",
                IconGlyph = "\uF012",
                Priority = 22
            },

            // === Analyze ===
            new()
            {
                Id = "archive-inspect-contents",
                Label = "List Archive Contents",
                Description = "Inspect compressed file tree, uncompressed sizes, and checksums without extracting.",
                Category = OperationCategory.Analyze,
                InputFormats = ArchiveFormats,
                OutputFormats = null,
                SupportsBatch = false,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "archive",
                IconGlyph = "\uE946",
                Priority = 30
            },
            new()
            {
                Id = "archive-test-integrity",
                Label = "Test Archive Integrity",
                Description = "Verify CRC32 and SHA checksums of all streams within archive to detect corruption.",
                Category = OperationCategory.Analyze,
                InputFormats = ArchiveFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "archive",
                IconGlyph = "\uE8FB",
                Priority = 31
            },

            // === Secure ===
            new()
            {
                Id = "archive-encrypt",
                Label = "Create Encrypted Archive (AES-256)",
                Description = "Compress and securely encrypt files with password-protected AES-256.",
                Category = OperationCategory.Secure,
                InputFormats = ["*"],
                OutputFormats = [".7z", ".zip"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = true,
                EngineId = "archive",
                IconGlyph = "\uE72E",
                Priority = 40
            },

            // === Optimize ===
            new()
            {
                Id = "archive-recompress-maximum",
                Label = "Recompress with Maximum Ratio",
                Description = "Decompress and recompress archive using Ultra LZMA2/Brotli compression.",
                Category = OperationCategory.Optimize,
                InputFormats = [".zip", ".tar", ".gz"],
                OutputFormats = [".7z", ".zip"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "archive",
                IconGlyph = "\uE93C",
                Priority = 50
            }
        };
    }
}
