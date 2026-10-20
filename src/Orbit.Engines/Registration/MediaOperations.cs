namespace Orbit.Engines.Registration;

using Orbit.Core.Models;

public static class MediaOperations
{
    private static readonly string[] VideoFormats = [".mp4", ".m4v", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".ts", ".3gp"];
    private static readonly string[] AudioFormats = [".mp3", ".wav", ".flac", ".aac", ".ogg", ".oga", ".m4a", ".wma", ".alac", ".opus"];
    private static readonly string[] AllMediaFormats = [".mp4", ".m4v", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".ts", ".3gp", ".mp3", ".wav", ".flac", ".aac", ".ogg", ".oga", ".m4a", ".wma", ".alac", ".opus"];

    public static IReadOnlyList<OperationDefinition> GetAll()
    {
        return new List<OperationDefinition>
        {
            // === Convert ===
            new()
            {
                Id = "media-video-to-mp4",
                Label = "Convert Video to MP4 (H.264 / AAC)",
                Description = "Transcode any video into universally compatible MP4 container.",
                Category = OperationCategory.Convert,
                InputFormats = [".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".ts", ".3gp"],
                OutputFormats = [".mp4"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "media",
                IconGlyph = "\uE714",
                KeyboardShortcut = "Ctrl+Shift+M",
                Priority = 10
            },
            new()
            {
                Id = "media-video-to-webm",
                Label = "Convert Video to WebM (VP9)",
                Description = "Transcode video to open WebM format optimized for browser streaming.",
                Category = OperationCategory.Convert,
                InputFormats = [".mp4", ".mov", ".mkv", ".avi", ".wmv"],
                OutputFormats = [".webm"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "media",
                IconGlyph = "\uE714",
                Priority = 11
            },
            new()
            {
                Id = "media-video-to-gif",
                Label = "Convert Video Clip to Animated GIF",
                Description = "Convert a video sequence into an animated looping GIF.",
                Category = OperationCategory.Convert,
                InputFormats = VideoFormats,
                OutputFormats = [".gif"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = true,
                RequiresConfirmation = false,
                EngineId = "media",
                IconGlyph = "\uE8B7",
                Priority = 12
            },
            new()
            {
                Id = "media-extract-audio-mp3",
                Label = "Extract Audio track to MP3",
                Description = "Demux and convert video soundtrack into a standalone MP3 audio file.",
                Category = OperationCategory.Convert,
                InputFormats = VideoFormats,
                OutputFormats = [".mp3"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "media",
                IconGlyph = "\uE8D6",
                Priority = 13
            },
            new()
            {
                Id = "media-extract-audio-wav",
                Label = "Extract Audio track to WAV",
                Description = "Demux video soundtrack into an uncompressed PCM WAV file.",
                Category = OperationCategory.Convert,
                InputFormats = VideoFormats,
                OutputFormats = [".wav"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "media",
                IconGlyph = "\uE8D6",
                Priority = 14
            },
            new()
            {
                Id = "media-audio-to-mp3",
                Label = "Convert Audio to MP3",
                Description = "Transcode audio stream into standard MP3 format.",
                Category = OperationCategory.Convert,
                InputFormats = [".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma", ".alac", ".opus"],
                OutputFormats = [".mp3"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "media",
                IconGlyph = "\uE8D6",
                Priority = 15
            },
            new()
            {
                Id = "media-audio-to-wav",
                Label = "Convert Audio to WAV",
                Description = "Decode compressed audio to uncompressed PCM audio.",
                Category = OperationCategory.Convert,
                InputFormats = [".mp3", ".flac", ".aac", ".ogg", ".m4a", ".wma"],
                OutputFormats = [".wav"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "media",
                IconGlyph = "\uE8D6",
                Priority = 16
            },
            new()
            {
                Id = "media-audio-to-flac",
                Label = "Convert Audio to FLAC Lossless",
                Description = "Encode audio with lossless Free Lossless Audio Codec.",
                Category = OperationCategory.Convert,
                InputFormats = [".wav", ".aiff", ".aif", ".m4a"],
                OutputFormats = [".flac"],
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "media",
                IconGlyph = "\uE8D6",
                Priority = 17
            },

            // === Edit ===
            new()
            {
                Id = "media-trim-video",
                Label = "Trim Video / Cut Segment",
                Description = "Select start and end timestamps to extract clip without full re-encoding.",
                Category = OperationCategory.Edit,
                InputFormats = VideoFormats,
                OutputFormats = null,
                SupportsBatch = false,
                ModifiesOriginal = false,
                RequiresWorkspace = true,
                RequiresConfirmation = false,
                EngineId = "media",
                IconGlyph = "\uE7AD",
                Priority = 20
            },
            new()
            {
                Id = "media-mute-audio",
                Label = "Mute / Strip Audio Track",
                Description = "Remove all audio streams from video container.",
                Category = OperationCategory.Edit,
                InputFormats = VideoFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "media",
                IconGlyph = "\uE74F",
                Priority = 21
            },
            new()
            {
                Id = "media-adjust-volume",
                Label = "Normalize / Boost Audio Volume",
                Description = "Normalize dynamic range or amplify gain across media tracks.",
                Category = OperationCategory.Edit,
                InputFormats = AllMediaFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "media",
                IconGlyph = "\uE767",
                Priority = 22
            },
            new()
            {
                Id = "media-resize-resolution",
                Label = "Scale Video Resolution (1080p / 720p / 480p)",
                Description = "Downscale video frame dimensions for bandwidth optimization.",
                Category = OperationCategory.Edit,
                InputFormats = VideoFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "media",
                IconGlyph = "\uE744",
                Priority = 23
            },

            // === Optimize ===
            new()
            {
                Id = "media-compress-video",
                Label = "Compress Video (Target Size)",
                Description = "Compress video using two-pass rate control to meet size constraints.",
                Category = OperationCategory.Optimize,
                InputFormats = VideoFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "media",
                IconGlyph = "\uE93C",
                Priority = 30
            },
            new()
            {
                Id = "media-compress-audio",
                Label = "Compress Audio (Bitrate Reduction)",
                Description = "Reduce audio bitrate (e.g., 320kbps to 128kbps) to conserve storage.",
                Category = OperationCategory.Optimize,
                InputFormats = AudioFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "media",
                IconGlyph = "\uE93C",
                Priority = 31
            },

            // === Analyze ===
            new()
            {
                Id = "media-inspect-streams",
                Label = "Media Stream Analysis (Codec / Bitrate / FPS)",
                Description = "Inspect video codecs, audio samplerate, frame rates, and stream metadata.",
                Category = OperationCategory.Analyze,
                InputFormats = AllMediaFormats,
                OutputFormats = null,
                SupportsBatch = true,
                ModifiesOriginal = false,
                RequiresWorkspace = false,
                RequiresConfirmation = false,
                EngineId = "media",
                IconGlyph = "\uE946",
                Priority = 40
            }
        };
    }
}
