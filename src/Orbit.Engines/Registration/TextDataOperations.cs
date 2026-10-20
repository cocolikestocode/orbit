namespace Orbit.Engines.Registration;

using Orbit.Core.Models;

public static class TextDataOperations
{
    private static readonly string[] TextFormats = [".txt", ".text", ".md", ".markdown", ".log", ".rtf"];
    private static readonly string[] CodeFormats = [".cs", ".js", ".ts", ".jsx", ".tsx", ".py", ".cpp", ".c", ".h", ".java", ".go", ".rs", ".rb", ".php", ".html", ".css", ".sql", ".sh", ".ps1"];
    private static readonly string[] DataFormats = [".json", ".json5", ".xml", ".yaml", ".yml", ".toml", ".csv", ".tsv", ".ini"];
    private static readonly string[] AllTextualFormats = [".txt", ".text", ".md", ".markdown", ".log", ".json", ".json5", ".xml", ".yaml", ".yml", ".toml", ".csv", ".tsv", ".ini", ".cs", ".js", ".ts", ".py", ".cpp", ".c", ".h", ".java", ".go", ".rs", ".sql", ".html", ".css"];

    public static IReadOnlyList<OperationDefinition> GetAll()
    {
        return new List<OperationDefinition>
        {
            // === Convert ===
            new()
            {
                Id = "data-json-to-csv",
                Label = "Convert JSON to CSV Table",
                Description = "Flatten JSON records into tabular comma-separated values (CSV).",
                Category = OperationCategory.Convert,
                InputFormats = [".json"],
                OutputFormats = [".csv"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "textdata",
                IconGlyph = "\uEA90",
                Priority = 10
            },
            new()
            {
                Id = "data-csv-to-json",
                Label = "Convert CSV to JSON",
                Description = "Parse tabular CSV rows into structured JSON array of objects.",
                Category = OperationCategory.Convert,
                InputFormats = [".csv", ".tsv"],
                OutputFormats = [".json"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "textdata",
                IconGlyph = "\uE943",
                Priority = 11
            },
            new()
            {
                Id = "data-json-to-yaml",
                Label = "Convert JSON to YAML",
                Description = "Transform JSON data into clean, human-readable YAML document.",
                Category = OperationCategory.Convert,
                InputFormats = [".json"],
                OutputFormats = [".yaml"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "textdata",
                IconGlyph = "\uE943",
                Priority = 12
            },
            new()
            {
                Id = "data-yaml-to-json",
                Label = "Convert YAML to JSON",
                Description = "Parse YAML specifications into formatted JSON data.",
                Category = OperationCategory.Convert,
                InputFormats = [".yaml", ".yml"],
                OutputFormats = [".json"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "textdata",
                IconGlyph = "\uE943",
                Priority = 13
            },
            new()
            {
                Id = "data-xml-to-json",
                Label = "Convert XML to JSON",
                Description = "Convert XML hierarchical trees into JSON notation.",
                Category = OperationCategory.Convert,
                InputFormats = [".xml"],
                OutputFormats = [".json"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "textdata",
                IconGlyph = "\uE943",
                Priority = 14
            },
            new()
            {
                Id = "data-base64-encode",
                Label = "Encode to Base64",
                Description = "Convert file contents or text into Base64 representation.",
                Category = OperationCategory.Convert,
                InputFormats = ["*"],
                OutputFormats = [".txt", ".b64"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "textdata",
                IconGlyph = "\uE8D2",
                Priority = 15
            },
            new()
            {
                Id = "data-base64-decode",
                Label = "Decode Base64 Data",
                Description = "Decode Base64 ASCII strings back to raw binary or text format.",
                Category = OperationCategory.Convert,
                InputFormats = [".txt", ".b64"],
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "textdata",
                IconGlyph = "\uE8D2",
                Priority = 16
            },

            // === Edit ===
            new()
            {
                Id = "data-format-json",
                Label = "Format & Prettify JSON",
                Description = "Indent and validate JSON structure with 2 or 4 spaces.",
                Category = OperationCategory.Edit,
                InputFormats = [".json"],
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "textdata",
                IconGlyph = "\uE943",
                Priority = 20
            },
            new()
            {
                Id = "data-minify-json",
                Label = "Minify JSON",
                Description = "Strip redundant whitespace and comments from JSON payloads.",
                Category = OperationCategory.Edit,
                InputFormats = [".json"],
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "textdata",
                IconGlyph = "\uE93C",
                Priority = 21
            },
            new()
            {
                Id = "data-format-xml",
                Label = "Format & Prettify XML",
                Description = "Format XML tags with proper indentation and schema validation.",
                Category = OperationCategory.Edit,
                InputFormats = [".xml"],
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "textdata",
                IconGlyph = "\uE943",
                Priority = 22
            },
            new()
            {
                Id = "text-normalize-line-endings",
                Label = "Convert Line Endings (CRLF / LF)",
                Description = "Standardize file line endings to Windows (CRLF) or Unix (LF).",
                Category = OperationCategory.Edit,
                InputFormats = AllTextualFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = true,
                RequiresWorkspace = false,
                RequiresConfirmation = true,
                EngineId = "textdata",
                IconGlyph = "\uE8D2",
                Priority = 23
            },
            new()
            {
                Id = "text-sort-lines",
                Label = "Sort & Deduplicate Lines",
                Description = "Sort lines alphabetically, numerically, or reverse, with optional unique filtering.",
                Category = OperationCategory.Edit,
                InputFormats = [".txt", ".csv", ".log", ".tsv"],
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "textdata",
                IconGlyph = "\uE8CB",
                Priority = 24
            },

            // === Analyze ===
            new()
            {
                Id = "text-diff-files",
                Label = "Diff Files (Compare)",
                Description = "Generate side-by-side or unified difference analysis between files.",
                Category = OperationCategory.Analyze,
                InputFormats = ["*"],
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = true,
                RequiresConfirmation = false,
                EngineId = "textdata",
                IconGlyph = "\uE8AB",
                Priority = 30
            },
            new()
            {
                Id = "text-count-lines-words",
                Label = "Count Lines, Words & Characters",
                Description = "Calculate textual metrics including blank lines, token counts, and file stats.",
                Category = OperationCategory.Analyze,
                InputFormats = AllTextualFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "textdata",
                IconGlyph = "\uE946",
                Priority = 31
            },
            new()
            {
                Id = "file-calculate-checksum",
                Label = "Calculate Hashes (SHA-256 / SHA-1 / MD5)",
                Description = "Compute cryptographic checksum digests for data integrity verification.",
                Category = OperationCategory.Analyze,
                InputFormats = ["*"],
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "textdata",
                IconGlyph = "\uE72E",
                Priority = 32
            }
        };
    }
}
