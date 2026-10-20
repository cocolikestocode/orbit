using System.Drawing;

namespace Orbit.Core.Models;

public enum GestureState
{
    Idle,
    DragDetected,
    ShiftHeld,
    OrbitActive,
    CategorySelected,
    ActionSelected,
    Committed,
    Cancelled,
    Processing,
    Complete
}

public record GestureEvent
{
    public required GestureState State { get; init; }
    public Point CursorPosition { get; init; }
    public string[]? FilePaths { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
