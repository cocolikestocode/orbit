namespace Orbit.Core.Services;

using Orbit.Core.Models;

public record ResolvedCapabilities
{
    public required FileIdentity[] Files { get; init; }
    public required Dictionary<OperationCategory, List<OperationDefinition>> CategorizedOperations { get; init; }
    public required OperationCategory[] AvailableCategories { get; init; }
    public bool IsBatchOperation { get; init; }
    public bool IsMixedSelection { get; init; }
}

public interface ICapabilityResolver
{
    ResolvedCapabilities Resolve(FileIdentity[] files);
    ResolvedCapabilities ResolveForCategory(FileIdentity[] files, OperationCategory category);
}
