namespace Orbit.Core.Tests;

using FluentAssertions;
using Orbit.Core.Models;
using Orbit.Core.Services.Impl;
using Xunit;

public class OperationRegistryTests
{
    private readonly OperationRegistry _registry = new();

    private static OperationDefinition CreateTestOperation(
        string id,
        string label,
        OperationCategory category,
        string[] inputFormats,
        int priority = 100,
        bool supportsBatch = true) =>
        new()
        {
            Id = id,
            Label = label,
            Category = category,
            InputFormats = inputFormats,
            Priority = priority,
            SupportsBatch = supportsBatch,
            EngineId = "test"
        };

    [Fact]
    public void Register_And_GetById_ShouldReturnRegisteredOperation()
    {
        var op = CreateTestOperation("op1", "Operation 1", OperationCategory.Convert, [".png"]);
        _registry.Register(op);

        var retrieved = _registry.GetById("op1");
        retrieved.Should().NotBeNull();
        retrieved!.Label.Should().Be("Operation 1");
    }

    [Fact]
    public void Register_ShouldBeCaseInsensitiveOnId()
    {
        var op = CreateTestOperation("op-case", "Case Op", OperationCategory.Edit, [".jpg"]);
        _registry.Register(op);

        _registry.GetById("OP-CASE").Should().NotBeNull();
        _registry.GetById("op-case").Should().NotBeNull();
    }

    [Fact]
    public void GetAll_ShouldSortByPriorityThenLabel()
    {
        var op1 = CreateTestOperation("op1", "Bravo", OperationCategory.Convert, [".png"], priority: 50);
        var op2 = CreateTestOperation("op2", "Alpha", OperationCategory.Convert, [".png"], priority: 50);
        var op3 = CreateTestOperation("op3", "Zulu", OperationCategory.Convert, [".png"], priority: 10);

        _registry.RegisterRange([op1, op2, op3]);

        var all = _registry.GetAll();
        all.Select(o => o.Id).Should().ContainInOrder("op3", "op2", "op1");
    }

    [Fact]
    public void GetForCategory_ShouldFilterCorrectly()
    {
        var op1 = CreateTestOperation("op-convert", "Convert Op", OperationCategory.Convert, [".png"]);
        var op2 = CreateTestOperation("op-edit", "Edit Op", OperationCategory.Edit, [".png"]);
        var op3 = CreateTestOperation("op-annotate", "Annotate Op", OperationCategory.Annotate, [".png"]);

        _registry.RegisterRange([op1, op2, op3]);

        var editOps = _registry.GetForCategory(OperationCategory.Edit);
        editOps.Should().HaveCount(1);
        editOps[0].Id.Should().Be("op-edit");
    }

    [Fact]
    public void GetForFile_ShouldMatchMatchingExtensions_AndWildcard()
    {
        var opPng = CreateTestOperation("op-png", "PNG only", OperationCategory.Convert, [".png"]);
        var opJpg = CreateTestOperation("op-jpg", "JPG only", OperationCategory.Convert, [".jpg"]);
        var opWildcard = CreateTestOperation("op-any", "Any format", OperationCategory.Create, ["*"]);

        _registry.RegisterRange([opPng, opJpg, opWildcard]);

        var fileIdentity = new FileIdentity
        {
            FilePath = "c:\\test\\sample.PNG",
            FileName = "sample.PNG",
            Extension = ".png",
            Family = FileFamily.Image,
            FileSize = 1024
        };

        var matching = _registry.GetForFile(fileIdentity);
        matching.Select(o => o.Id).Should().Contain(["op-png", "op-any"]);
        matching.Select(o => o.Id).Should().NotContain("op-jpg");
    }

    [Fact]
    public void GetAvailableCategories_ShouldReturnCategoriesWithAvailableOperations()
    {
        var opConvert = CreateTestOperation("op-c", "Convert", OperationCategory.Convert, [".png"]);
        var opOptimize = CreateTestOperation("op-o", "Optimize", OperationCategory.Optimize, [".png"]);
        var opAudioOnly = CreateTestOperation("op-a", "Audio Op", OperationCategory.Edit, [".mp3"]);

        _registry.RegisterRange([opConvert, opOptimize, opAudioOnly]);

        var file = new FileIdentity
        {
            FilePath = "image.png",
            FileName = "image.png",
            Extension = ".png",
            Family = FileFamily.Image,
            FileSize = 500
        };

        var categories = _registry.GetAvailableCategories([file]);
        categories.Should().Contain([OperationCategory.Convert, OperationCategory.Optimize]);
        categories.Should().NotContain(OperationCategory.Edit);
    }
}
