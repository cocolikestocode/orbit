namespace Orbit.Engines.Registration;

using Orbit.Core.Models;

public static class AllOperations
{
    public static IReadOnlyList<OperationDefinition> GetAll()
    {
        var all = new List<OperationDefinition>();
        all.AddRange(ImageOperations.GetAll());
        all.AddRange(DocumentOperations.GetAll());
        all.AddRange(ArchiveOperations.GetAll());
        all.AddRange(MediaOperations.GetAll());
        all.AddRange(TextDataOperations.GetAll());
        return all;
    }
}
