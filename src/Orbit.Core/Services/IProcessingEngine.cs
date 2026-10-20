namespace Orbit.Core.Services;

using Orbit.Core.Models;

public record EngineCapabilities
{
    public required string EngineId { get; init; }
    public required string[] SupportedInputFormats { get; init; }
    public required string[] SupportedOutputFormats { get; init; }
    public required OperationCategory[] SupportedCategories { get; init; }
    public bool SupportsBatch { get; init; }
}

public interface IProcessingEngine
{
    string EngineId { get; }
    string DisplayName { get; }
    bool IsAvailable { get; }
    Task<EngineCapabilities> GetCapabilitiesAsync();
    Task<OperationResult> ExecuteAsync(
        OperationRequest request,
        IProgress<OperationProgress> progress,
        CancellationToken cancellationToken = default);
    Task<bool> ValidateAsync(OperationRequest request);
}
