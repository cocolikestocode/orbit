namespace Orbit.Core.Models;

public enum OperationCategory
{
    Convert,
    Edit,
    Annotate,
    Optimize,
    Organize,
    Extract,
    Secure,
    Analyze,
    Create,
    Share
}

public record OperationDefinition
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public required OperationCategory Category { get; init; }
    public required string[] InputFormats { get; init; }
    public string[]? OutputFormats { get; init; }
    public bool SupportsBatch { get; init; }
    public bool ModifiesOriginal { get; init; }
    public bool RequiresWorkspace { get; init; }
    public bool RequiresConfirmation { get; init; }
    public required string EngineId { get; init; }
    public string? IconGlyph { get; init; }
    public string? KeyboardShortcut { get; init; }
    public int Priority { get; init; } = 100;
}
