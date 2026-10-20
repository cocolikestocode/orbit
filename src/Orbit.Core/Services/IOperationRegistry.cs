namespace Orbit.Core.Services;

using Orbit.Core.Models;

public interface IOperationRegistry
{
    void Register(OperationDefinition operation);
    void RegisterRange(IEnumerable<OperationDefinition> operations);
    IReadOnlyList<OperationDefinition> GetAll();
    IReadOnlyList<OperationDefinition> GetForFileFamily(FileFamily family);
    IReadOnlyList<OperationDefinition> GetForCategory(OperationCategory category);
    IReadOnlyList<OperationDefinition> GetForFile(FileIdentity file);
    IReadOnlyList<OperationCategory> GetAvailableCategories(FileIdentity[] files);
    OperationDefinition? GetById(string id);
}
