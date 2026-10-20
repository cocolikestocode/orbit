namespace Orbit.Core.Services.Impl;

using System;
using System.Collections.Generic;
using System.Linq;
using Orbit.Core.Models;

public class CapabilityResolver : ICapabilityResolver
{
    private readonly IOperationRegistry _registry;

    public CapabilityResolver(IOperationRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public ResolvedCapabilities Resolve(FileIdentity[] files)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (files.Length == 0)
        {
            return new ResolvedCapabilities
            {
                Files = files,
                CategorizedOperations = new Dictionary<OperationCategory, List<OperationDefinition>>(),
                AvailableCategories = Array.Empty<OperationCategory>(),
                IsBatchOperation = false,
                IsMixedSelection = false
            };
        }

        var isBatch = files.Length > 1;
        var distinctFamilies = files.Select(f => f.Family).Distinct().ToList();
        var isMixed = distinctFamilies.Count > 1;

        IReadOnlyList<OperationDefinition> matchingOps;

        if (files.Length == 1)
        {
            matchingOps = _registry.GetForFile(files[0]);
        }
        else
        {
            // Batch: Find common operations that support batch and match ALL files
            var allOps = _registry.GetAll();
            matchingOps = allOps
                .Where(op => op.SupportsBatch && files.All(file => MatchesFile(op, file)))
                .ToList();
        }

        var categorized = matchingOps
            .GroupBy(op => op.Category)
            .OrderBy(g => (int)g.Key)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(op => op.Priority).ThenBy(op => op.Label, StringComparer.OrdinalIgnoreCase).ToList()
            );

        var availableCategories = categorized.Keys.ToArray();

        return new ResolvedCapabilities
        {
            Files = files,
            CategorizedOperations = categorized,
            AvailableCategories = availableCategories,
            IsBatchOperation = isBatch,
            IsMixedSelection = isMixed
        };
    }

    public ResolvedCapabilities ResolveForCategory(FileIdentity[] files, OperationCategory category)
    {
        var resolved = Resolve(files);

        var filteredDict = new Dictionary<OperationCategory, List<OperationDefinition>>();
        if (resolved.CategorizedOperations.TryGetValue(category, out var list) && list.Count > 0)
        {
            filteredDict[category] = list;
        }

        return new ResolvedCapabilities
        {
            Files = resolved.Files,
            CategorizedOperations = filteredDict,
            AvailableCategories = filteredDict.Keys.ToArray(),
            IsBatchOperation = resolved.IsBatchOperation,
            IsMixedSelection = resolved.IsMixedSelection
        };
    }

    private static bool MatchesFile(OperationDefinition op, FileIdentity file)
    {
        var ext = file.Extension.StartsWith('.') ? file.Extension : "." + file.Extension;

        return op.InputFormats.Any(format =>
        {
            if (string.Equals(format, "*", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(format, file.Family.ToString(), StringComparison.OrdinalIgnoreCase)) return true;

            var normalizedFormat = format.StartsWith('.') ? format : "." + format;
            return string.Equals(normalizedFormat, ext, StringComparison.OrdinalIgnoreCase);
        });
    }
}
