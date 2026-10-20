namespace Orbit.Engines.Registration;

using Orbit.Core.Models;

public static class ImageOperations
{
    private static readonly string[] CommonRasterFormats = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tiff", ".tif"];
    private static readonly string[] AllImageFormats = [".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".webp", ".gif", ".bmp", ".dib", ".tiff", ".tif", ".heic", ".heif", ".avif", ".svg", ".ico"];

    public static IReadOnlyList<OperationDefinition> GetAll()
    {
        return new List<OperationDefinition>
        {
            // === Convert ===
            new()
            {
                Id = "image-png-to-jpg",
                Label = "Convert to JPG",
                Description = "Convert PNG image to JPEG format with optional quality setting.",
                Category = OperationCategory.Convert,
                InputFormats = [".png"],
                OutputFormats = [".jpg"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE790",
                KeyboardShortcut = "Ctrl+Shift+J",
                Priority = 10
            },
            new()
            {
                Id = "image-png-to-webp",
                Label = "Convert to WebP",
                Description = "Convert PNG image to modern web-optimized WebP format.",
                Category = OperationCategory.Convert,
                InputFormats = [".png"],
                OutputFormats = [".webp"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uEB9F",
                KeyboardShortcut = "Ctrl+Shift+W",
                Priority = 11
            },
            new()
            {
                Id = "image-jpg-to-png",
                Label = "Convert to PNG",
                Description = "Convert JPEG image to lossless PNG format.",
                Category = OperationCategory.Convert,
                InputFormats = [".jpg", ".jpeg", ".jfif"],
                OutputFormats = [".png"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uEB9F",
                KeyboardShortcut = "Ctrl+Shift+P",
                Priority = 10
            },
            new()
            {
                Id = "image-jpg-to-webp",
                Label = "Convert to WebP",
                Description = "Convert JPEG image to smaller, web-optimized WebP format.",
                Category = OperationCategory.Convert,
                InputFormats = [".jpg", ".jpeg", ".jfif"],
                OutputFormats = [".webp"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uEB9F",
                KeyboardShortcut = "Ctrl+Shift+W",
                Priority = 11
            },
            new()
            {
                Id = "image-webp-to-png",
                Label = "Convert to PNG",
                Description = "Decode WebP image into lossless PNG format.",
                Category = OperationCategory.Convert,
                InputFormats = [".webp"],
                OutputFormats = [".png"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uEB9F",
                KeyboardShortcut = "Ctrl+Shift+P",
                Priority = 10
            },
            new()
            {
                Id = "image-webp-to-jpg",
                Label = "Convert to JPG",
                Description = "Convert WebP image to standard JPEG format.",
                Category = OperationCategory.Convert,
                InputFormats = [".webp"],
                OutputFormats = [".jpg"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE790",
                KeyboardShortcut = "Ctrl+Shift+J",
                Priority = 11
            },
            new()
            {
                Id = "image-heic-to-jpg",
                Label = "Convert HEIC to JPG",
                Description = "Convert Apple High Efficiency HEIC photo to universally compatible JPEG.",
                Category = OperationCategory.Convert,
                InputFormats = [".heic", ".heif"],
                OutputFormats = [".jpg"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE790",
                Priority = 12
            },
            new()
            {
                Id = "image-heic-to-png",
                Label = "Convert HEIC to PNG",
                Description = "Convert Apple High Efficiency HEIC photo to lossless PNG.",
                Category = OperationCategory.Convert,
                InputFormats = [".heic", ".heif"],
                OutputFormats = [".png"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uEB9F",
                Priority = 13
            },
            new()
            {
                Id = "image-svg-to-png",
                Label = "Render SVG to PNG",
                Description = "Rasterize scalable vector graphic (SVG) into crisp high-resolution PNG.",
                Category = OperationCategory.Convert,
                InputFormats = [".svg", ".svgz"],
                OutputFormats = [".png"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE790",
                Priority = 14
            },
            new()
            {
                Id = "image-gif-to-images",
                Label = "Extract GIF Frames",
                Description = "Extract individual animation frames from an animated GIF file.",
                Category = OperationCategory.Convert,
                InputFormats = [".gif"],
                OutputFormats = [".png"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE8B7",
                Priority = 15
            },
            new()
            {
                Id = "image-images-to-pdf",
                Label = "Combine Images to PDF",
                Description = "Compile selected images into a single multi-page PDF document.",
                Category = OperationCategory.Convert,
                InputFormats = CommonRasterFormats,
                OutputFormats = [".pdf"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uEA90",
                Priority = 16
            },
            new()
            {
                Id = "image-images-to-ico",
                Label = "Convert to Windows Icon (.ico)",
                Description = "Generate multi-resolution Windows ICO icon file from source images.",
                Category = OperationCategory.Convert,
                InputFormats = [".png", ".jpg", ".jpeg", ".webp", ".bmp"],
                OutputFormats = [".ico"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE790",
                Priority = 17
            },

            // === Edit ===
            new()
            {
                Id = "image-editor",
                Label = "Photo Studio",
                Description = "Open interactive photo studio with live adjustments, filters, watermarks, and crop.",
                Category = OperationCategory.Edit,
                InputFormats = CommonRasterFormats,
                OutputFormats = null,
                SupportsBatch = false,
                ModifiesOriginal = false,
                RequiresWorkspace = true,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE7AD",
                KeyboardShortcut = "E",
                Priority = 20
            },
            new()
            {
                Id = "image-crop",
                Label = "Crop Image",
                Description = "Crop borders or specify an exact bounding box.",
                Category = OperationCategory.Edit,
                InputFormats = CommonRasterFormats,
                OutputFormats = null,
                SupportsBatch = false,
                ModifiesOriginal = false,
                RequiresWorkspace = true,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE7AD",
                KeyboardShortcut = "C",
                Priority = 21
            },
            new()
            {
                Id = "image-adjust",
                Label = "Adjust Light & Colors",
                Description = "Fine-tune brightness, contrast, saturation, and blur with live canvas preview.",
                Category = OperationCategory.Edit,
                InputFormats = CommonRasterFormats,
                OutputFormats = null,
                SupportsBatch = false,
                ModifiesOriginal = false,
                RequiresWorkspace = true,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE790",
                Priority = 22
            },
            new()
            {
                Id = "image-add-watermark",
                Label = "Add Watermark",
                Description = "Overlay custom text or logo watermark with configurable opacity and live placement.",
                Category = OperationCategory.Edit,
                InputFormats = CommonRasterFormats,
                OutputFormats = null,
                SupportsBatch = false,
                ModifiesOriginal = false,
                RequiresWorkspace = true,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE8AC",
                Priority = 23
            },
            new()
            {
                Id = "image-rotate",
                Label = "Rotate 90° CW",
                Description = "Rotate image 90° clockwise in-place.",
                Category = OperationCategory.Edit,
                InputFormats = CommonRasterFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE7AD",
                KeyboardShortcut = "R",
                Priority = 24
            },
            new()
            {
                Id = "image-flip",
                Label = "Flip Horizontal",
                Description = "Mirror the image horizontally.",
                Category = OperationCategory.Edit,
                InputFormats = CommonRasterFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE748",
                Priority = 25
            },
            new()
            {
                Id = "image-resize",
                Label = "Resize (50% Scale)",
                Description = "Scale dimensions down to 50% maintaining aspect ratio.",
                Category = OperationCategory.Edit,
                InputFormats = CommonRasterFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE744",
                Priority = 26
            },

            // === Annotate ===
            new()
            {
                Id = "image-annotate-freehand",
                Label = "Freehand Draw (Pen)",
                Description = "Draw freehand markings and brush strokes on the image.",
                Category = OperationCategory.Annotate,
                InputFormats = CommonRasterFormats,
                OutputFormats = null,
                SupportsBatch = false,
                ModifiesOriginal = false,
                RequiresWorkspace = true,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uED63",
                KeyboardShortcut = "P",
                Priority = 30
            },
            new()
            {
                Id = "image-annotate-arrow",
                Label = "Pointer Arrow",
                Description = "Place callout arrows pointing at key visual elements.",
                Category = OperationCategory.Annotate,
                InputFormats = CommonRasterFormats,
                OutputFormats = null,
                SupportsBatch = false,
                ModifiesOriginal = false,
                RequiresWorkspace = true,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE74A",
                KeyboardShortcut = "A",
                Priority = 31
            },
            new()
            {
                Id = "image-annotate-text",
                Label = "Text Callout",
                Description = "Overlay formatted text labels and captions.",
                Category = OperationCategory.Annotate,
                InputFormats = CommonRasterFormats,
                OutputFormats = null,
                SupportsBatch = false,
                ModifiesOriginal = false,
                RequiresWorkspace = true,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE8D2",
                KeyboardShortcut = "T",
                Priority = 32
            },
            new()
            {
                Id = "image-blur-region",
                Label = "Blur / Privacy Redact",
                Description = "Apply Gaussian blur to redact sensitive faces, passwords, or credentials.",
                Category = OperationCategory.Annotate,
                InputFormats = CommonRasterFormats,
                OutputFormats = null,
                SupportsBatch = false,
                ModifiesOriginal = false,
                RequiresWorkspace = true,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE709",
                KeyboardShortcut = "B",
                Priority = 33
            },
            new()
            {
                Id = "image-annotate-step",
                Label = "Numbered Step Badges",
                Description = "Click to place sequential numbered badges (1, 2, 3...) for guides and walkthroughs.",
                Category = OperationCategory.Annotate,
                InputFormats = CommonRasterFormats,
                OutputFormats = null,
                SupportsBatch = false,
                ModifiesOriginal = false,
                RequiresWorkspace = true,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE896",
                KeyboardShortcut = "S",
                Priority = 34
            },
            new()
            {
                Id = "image-pixelate-region",
                Label = "Pixelate Region",
                Description = "Redact confidential content using a mosaic pixelation filter.",
                Category = OperationCategory.Annotate,
                InputFormats = CommonRasterFormats,
                OutputFormats = null,
                SupportsBatch = false,
                ModifiesOriginal = false,
                RequiresWorkspace = true,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE74C",
                Priority = 35
            },

            // === Optimize ===
            new()
            {
                Id = "image-compress-lossy",
                Label = "Lossy Compression",
                Description = "Significantly reduce file size with imperceptible perceptual quality tradeoff.",
                Category = OperationCategory.Optimize,
                InputFormats = [".jpg", ".jpeg", ".webp", ".png"],
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE93C",
                Priority = 40
            },
            new()
            {
                Id = "image-compress-lossless",
                Label = "Lossless Compression",
                Description = "Optimize image payload and Huffman tables without losing a single pixel.",
                Category = OperationCategory.Optimize,
                InputFormats = [".png", ".webp", ".gif"],
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE93C",
                Priority = 41
            },
            new()
            {
                Id = "image-resize-to-target",
                Label = "Fit to Target Size (KB/MB)",
                Description = "Automatically adjust compression and dimensions to meet target file size limit.",
                Category = OperationCategory.Optimize,
                InputFormats = CommonRasterFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE744",
                Priority = 42
            },
            new()
            {
                Id = "image-strip-metadata",
                Label = "Strip EXIF Metadata",
                Description = "Remove GPS location, camera equipment, and personal identifying tags.",
                Category = OperationCategory.Optimize,
                InputFormats = [".jpg", ".jpeg", ".png", ".webp", ".tiff", ".tif"],
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE74D",
                Priority = 43
            },

            // === Analyze ===
            new()
            {
                Id = "image-info",
                Label = "Inspect Image Properties",
                Description = "View exact dimensions, color depth, pixel density, and color space.",
                Category = OperationCategory.Analyze,
                InputFormats = AllImageFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE946",
                KeyboardShortcut = "Ctrl+I",
                Priority = 50
            },
            new()
            {
                Id = "image-exif-data",
                Label = "View EXIF & Camera Info",
                Description = "Examine camera shutter, ISO, aperture, lens profile, and GPS coordinates.",
                Category = OperationCategory.Analyze,
                InputFormats = [".jpg", ".jpeg", ".tiff", ".tif", ".heic", ".dng", ".cr2", ".nef", ".raw"],
                OutputFormats = null,
                SupportsBatch = false,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE722",
                Priority = 51
            },
            new()
            {
                Id = "image-color-profile-info",
                Label = "Color Profile & Histogram",
                Description = "Analyze embedded ICC profile, channel histograms, and dominant color palette.",
                Category = OperationCategory.Analyze,
                InputFormats = [".jpg", ".jpeg", ".png", ".tiff", ".tif", ".webp"],
                OutputFormats = null,
                SupportsBatch = false,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE790",
                Priority = 52
            },

            // === Organize ===
            new()
            {
                Id = "image-rename-by-dimensions",
                Label = "Rename by Dimensions (e.g., photo_1920x1080)",
                Description = "Append or prepend image pixel dimensions to file names.",
                Category = OperationCategory.Organize,
                InputFormats = AllImageFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = true,
                RequiresWorkspace = false,
                RequiresConfirmation = true,
                EngineId = "image",
                IconGlyph = "\uE8AC",
                Priority = 60
            },
            new()
            {
                Id = "image-rename-by-date",
                Label = "Rename by Date Taken (EXIF)",
                Description = "Standardize filenames based on original camera capture timestamp.",
                Category = OperationCategory.Organize,
                InputFormats = [".jpg", ".jpeg", ".heic", ".tiff", ".raw", ".cr2", ".nef"],
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = true,
                RequiresWorkspace = false,
                RequiresConfirmation = true,
                EngineId = "image",
                IconGlyph = "\uE787",
                Priority = 61
            },
            new()
            {
                Id = "image-generate-thumbnail",
                Label = "Generate Thumbnails",
                Description = "Generate square or proportional preview thumbnails.",
                Category = OperationCategory.Organize,
                InputFormats = AllImageFormats,
                OutputFormats = [".jpg", ".png", ".webp"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE790",
                Priority = 62
            },
            new()
            {
                Id = "image-create-contact-sheet",
                Label = "Generate Contact Sheet",
                Description = "Arrange multiple image thumbnails into an organized grid preview sheet.",
                Category = OperationCategory.Organize,
                InputFormats = CommonRasterFormats,
                OutputFormats = [".jpg", ".png", ".pdf"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "image",
                IconGlyph = "\uE8B9",
                Priority = 63
            }
        };
    }
}
