namespace Orbit.Core.Models;

public enum FileFamily
{
    Image,
    Document,
    Video,
    Audio,
    Archive,
    Spreadsheet,
    Presentation,
    Code,
    Text,
    Data,
    Font,
    Design,
    ThreeDimensional,
    Scientific,
    Ebook,
    Email,
    Certificate,
    Executable,
    DiskImage,
    Database,
    Log,
    Folder,
    Unknown
}

public record FileIdentity
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string Extension { get; init; }
    public string? MimeType { get; init; }
    public required FileFamily Family { get; init; }
    public required long FileSize { get; init; }
    public bool IsReadable { get; init; } = true;
    public bool HasTransparency { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}
