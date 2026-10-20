namespace Orbit.Core.Services.Impl;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Orbit.Core.Models;

public class OperationRegistry : IOperationRegistry
{
    private readonly ConcurrentDictionary<string, OperationDefinition> _operations = new(StringComparer.OrdinalIgnoreCase);
    private readonly IFileIdentifier _fileIdentifier;

    public OperationRegistry(IFileIdentifier? fileIdentifier = null)
    {
        _fileIdentifier = fileIdentifier ?? new FileIdentifier();
    }

    public void Register(OperationDefinition operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _operations[operation.Id] = operation;
    }

    public void RegisterRange(IEnumerable<OperationDefinition> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        foreach (var op in operations)
        {
            Register(op);
        }
    }

    public IReadOnlyList<OperationDefinition> GetAll()
    {
        return _operations.Values
            .OrderBy(o => o.Priority)
            .ThenBy(o => o.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public OperationDefinition? GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return _operations.TryGetValue(id, out var op) ? op : null;
    }

    public IReadOnlyList<OperationDefinition> GetForCategory(OperationCategory category)
    {
        return _operations.Values
            .Where(o => o.Category == category)
            .OrderBy(o => o.Priority)
            .ThenBy(o => o.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<OperationDefinition> GetForFile(FileIdentity file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return _operations.Values
            .Where(op => MatchesFile(op, file))
            .OrderBy(o => o.Priority)
            .ThenBy(o => o.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<OperationDefinition> GetForFileFamily(FileFamily family)
    {
        return _operations.Values
            .Where(op =>
            {
                return op.InputFormats.Any(format =>
                {
                    if (string.Equals(format, "*", StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(format, family.ToString(), StringComparison.OrdinalIgnoreCase)) return true;

                    var detected = _fileIdentifier.DetectFamily(format);
                    return detected == family;
                });
            })
            .OrderBy(o => o.Priority)
            .ThenBy(o => o.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<OperationCategory> GetAvailableCategories(FileIdentity[] files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Length == 0) return Array.Empty<OperationCategory>();

        var matchingOperations = GetMatchingOperationsForFiles(files);
        return matchingOperations
            .Select(o => o.Category)
            .Distinct()
            .OrderBy(c => (int)c)
            .ToList();
    }

    public IReadOnlyList<OperationDefinition> GetMatchingOperationsForFiles(FileIdentity[] files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Length == 0) return Array.Empty<OperationDefinition>();

        if (files.Length == 1)
        {
            return GetForFile(files[0]);
        }

        // For multiple files, operation must support batch and match ALL files
        return _operations.Values
            .Where(op => op.SupportsBatch && files.All(f => MatchesFile(op, f)))
            .OrderBy(o => o.Priority)
            .ThenBy(o => o.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
