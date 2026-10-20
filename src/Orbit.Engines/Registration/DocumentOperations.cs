namespace Orbit.Engines.Registration;

using Orbit.Core.Models;

public static class DocumentOperations
{
    private static readonly string[] CommonDocFormats = [".pdf", ".docx", ".doc", ".rtf", ".odt", ".txt", ".md"];

    public static IReadOnlyList<OperationDefinition> GetAll()
    {
        return new List<OperationDefinition>
        {
            // === Convert ===
            new()
            {
                Id = "doc-docx-to-pdf",
                Label = "Convert Word to PDF",
                Description = "Render DOCX document into a high-fidelity PDF file.",
                Category = OperationCategory.Convert,
                InputFormats = [".docx", ".doc", ".odt", ".rtf"],
                OutputFormats = [".pdf"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "document",
                IconGlyph = "\uEA90",
                KeyboardShortcut = "Ctrl+Shift+D",
                Priority = 10
            },
            new()
            {
                Id = "doc-pdf-to-docx",
                Label = "Convert PDF to Word",
                Description = "Reconstruct editable Word document layout and text from PDF.",
                Category = OperationCategory.Convert,
                InputFormats = [".pdf"],
                OutputFormats = [".docx"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "document",
                IconGlyph = "\uE8A5",
                Priority = 11
            },
            new()
            {
                Id = "doc-pdf-to-txt",
                Label = "Convert PDF to Plain Text",
                Description = "Extract all raw readable text from PDF pages.",
                Category = OperationCategory.Convert,
                InputFormats = [".pdf"],
                OutputFormats = [".txt"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "document",
                IconGlyph = "\uE8D2",
                Priority = 12
            },
            new()
            {
                Id = "doc-pdf-to-images",
                Label = "Convert PDF Pages to Images",
                Description = "Render each page of a PDF document to individual PNG or JPEG images.",
                Category = OperationCategory.Convert,
                InputFormats = [".pdf"],
                OutputFormats = [".png", ".jpg"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "document",
                IconGlyph = "\uEB9F",
                Priority = 13
            },
            new()
            {
                Id = "doc-markdown-to-pdf",
                Label = "Export Markdown to PDF",
                Description = "Format GitHub-flavored Markdown into a styled, publication-ready PDF.",
                Category = OperationCategory.Convert,
                InputFormats = [".md", ".markdown"],
                OutputFormats = [".pdf"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "document",
                IconGlyph = "\uEA90",
                Priority = 14
            },
            new()
            {
                Id = "doc-markdown-to-html",
                Label = "Convert Markdown to HTML",
                Description = "Render Markdown to standalone HTML with syntax-highlighted code blocks.",
                Category = OperationCategory.Convert,
                InputFormats = [".md", ".markdown"],
                OutputFormats = [".html"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "document",
                IconGlyph = "\uE774",
                Priority = 15
            },

            // === Edit ===
            new()
            {
                Id = "doc-rotate-pages",
                Label = "Rotate PDF Pages",
                Description = "Rotate all or selected PDF pages by 90, 180, or 270 degrees.",
                Category = OperationCategory.Edit,
                InputFormats = [".pdf"],
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "document",
                IconGlyph = "\uE7AD",
                Priority = 20
            },
            new()
            {
                Id = "doc-reorder-pages",
                Label = "Reorder PDF Pages",
                Description = "Interactively rearrange the page sequence of a PDF document.",
                Category = OperationCategory.Edit,
                InputFormats = [".pdf"],
                OutputFormats = null,
                SupportsBatch = false,
                ModifiesOriginal = false,
                RequiresWorkspace = true,
                RequiresConfirmation = false,
                EngineId = "document",
                IconGlyph = "\uE8D2",
                Priority = 21
            },

            // === Extract ===
            new()
            {
                Id = "doc-extract-images",
                Label = "Extract Embedded Images",
                Description = "Extract all bitmap and vector images embedded in the document.",
                Category = OperationCategory.Extract,
                InputFormats = [".pdf", ".docx"],
                OutputFormats = [".png", ".jpg"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "document",
                IconGlyph = "\uEB9F",
                Priority = 30
            },
            new()
            {
                Id = "doc-split-pages",
                Label = "Split PDF into Single Pages",
                Description = "Break a multi-page PDF document into individual single-page documents.",
                Category = OperationCategory.Extract,
                InputFormats = [".pdf"],
                OutputFormats = [".pdf"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "document",
                IconGlyph = "\uE8B7",
                Priority = 31
            },

            // === Organize ===
            new()
            {
                Id = "doc-merge-pdfs",
                Label = "Merge PDF Files",
                Description = "Combine multiple PDF documents into a single consolidated document.",
                Category = OperationCategory.Organize,
                InputFormats = [".pdf"],
                OutputFormats = [".pdf"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "document",
                IconGlyph = "\uE8F1",
                Priority = 40
            },

            // === Secure ===
            new()
            {
                Id = "doc-encrypt-pdf",
                Label = "Password Protect PDF",
                Description = "Add AES-256 encryption and require a password to view or edit the PDF.",
                Category = OperationCategory.Secure,
                InputFormats = [".pdf"],
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = true,
                EngineId = "document",
                IconGlyph = "\uE72E",
                Priority = 50
            },
            new()
            {
                Id = "doc-decrypt-pdf",
                Label = "Remove PDF Password",
                Description = "Unlock an encrypted PDF document by providing its owner password.",
                Category = OperationCategory.Secure,
                InputFormats = [".pdf"],
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = true,
                EngineId = "document",
                IconGlyph = "\uE785",
                Priority = 51
            },

            // === Annotate ===
            new()
            {
                Id = "doc-sign-document",
                Label = "Digitally Sign PDF",
                Description = "Apply digital cryptographic signature or signature visual stamp to PDF.",
                Category = OperationCategory.Annotate,
                InputFormats = [".pdf"],
                OutputFormats = null,
                SupportsBatch = false,
                ModifiesOriginal = false,
                RequiresWorkspace = true,
                RequiresConfirmation = false,
                EngineId = "document",
                IconGlyph = "\uED63",
                Priority = 60
            },

            // === Optimize ===
            new()
            {
                Id = "doc-compress-pdf",
                Label = "Compress PDF Document",
                Description = "Downsample embedded images and clean up unreferenced PDF streams.",
                Category = OperationCategory.Optimize,
                InputFormats = [".pdf"],
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "document",
                IconGlyph = "\uE93C",
                Priority = 70
            },

            // === Analyze ===
            new()
            {
                Id = "doc-word-count",
                Label = "Document Statistics & Word Count",
                Description = "Analyze word count, character count, page count, and reading time.",
                Category = OperationCategory.Analyze,
                InputFormats = CommonDocFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "document",
                IconGlyph = "\uE946",
                Priority = 80
            }
        };
    }
}
