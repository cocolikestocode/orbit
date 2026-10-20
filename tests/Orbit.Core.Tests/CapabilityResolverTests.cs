namespace Orbit.Core.Tests;

using FluentAssertions;
using Orbit.Core.Models;
using Orbit.Core.Services.Impl;
using Xunit;

public class CapabilityResolverTests
{
    private readonly OperationRegistry _registry;
    private readonly CapabilityResolver _resolver;

    public CapabilityResolverTests()
    {
        _registry = new OperationRegistry();
        _resolver = new CapabilityResolver(_registry);

        // Seed registry with sample operations
        _registry.RegisterRange([
            new OperationDefinition
            {
                Id = "image-convert-jpg",
                Label = "Convert to JPG",
                Category = OperationCategory.Convert,
                InputFormats = [".png"],
                OutputFormats = [".jpg"],
                SupportsBatch = true,
                EngineId = "image"
            },
            new OperationDefinition
            {
                Id = "image-crop-single",
                Label = "Interactive Crop",
                Category = OperationCategory.Edit,
                InputFormats = [".png", ".jpg"],
                SupportsBatch = false, // Does NOT support batch
                EngineId = "image"
            },
            new OperationDefinition
            {
                Id = "image-batch-resize",
                Label = "Batch Resize",
                Category = OperationCategory.Edit,
                InputFormats = [".png", ".jpg"],
                SupportsBatch = true,
                EngineId = "image"
            },
            new OperationDefinition
            {
                Id = "doc-merge-pdf",
                Label = "Merge PDFs",
                Category = OperationCategory.Organize,
                InputFormats = [".pdf"],
                SupportsBatch = true,
                EngineId = "doc"
            },
            new OperationDefinition
            {
                Id = "universal-zip",
                Label = "Compress to ZIP",
                Category = OperationCategory.Create,
                InputFormats = ["*"],
                SupportsBatch = true,
                EngineId = "archive"
            },
            new OperationDefinition
            {
                Id = "universal-hash",
                Label = "Calculate SHA256",
                Category = OperationCategory.Analyze,
                InputFormats = ["*"],
                SupportsBatch = true,
                EngineId = "textdata"
            }
        ]);
    }

    private static FileIdentity MakeFile(string name, string ext, FileFamily family) =>
        new()
        {
            FilePath = "c:\\files\\" + name,
            FileName = name,
            Extension = ext,
            Family = family,
            FileSize = 2048
        };

    [Fact]
    public void Resolve_EmptyFileList_ShouldReturnEmptyCapabilities()
    {
        var result = _resolver.Resolve(Array.Empty<FileIdentity>());

        result.Files.Should().BeEmpty();
        result.AvailableCategories.Should().BeEmpty();
        result.CategorizedOperations.Should().BeEmpty();
        result.IsBatchOperation.Should().BeFalse();
        result.IsMixedSelection.Should().BeFalse();
    }

    [Fact]
    public void Resolve_SingleFile_ShouldIncludeNonBatchAndBatchOperations()
    {
        var file = MakeFile("photo.png", ".png", FileFamily.Image);

        var result = _resolver.Resolve([file]);

        result.IsBatchOperation.Should().BeFalse();
        result.IsMixedSelection.Should().BeFalse();
        result.AvailableCategories.Should().Contain([OperationCategory.Convert, OperationCategory.Edit, OperationCategory.Create, OperationCategory.Analyze]);

        // Single file supports interactive crop (SupportsBatch=false)
        result.CategorizedOperations[OperationCategory.Edit].Select(o => o.Id).Should().Contain("image-crop-single");
    }

    [Fact]
    public void Resolve_BatchSameFormat_ShouldFilterOutNonBatchOperations()
    {
        var file1 = MakeFile("photo1.png", ".png", FileFamily.Image);
        var file2 = MakeFile("photo2.png", ".png", FileFamily.Image);

        var result = _resolver.Resolve([file1, file2]);

        result.IsBatchOperation.Should().BeTrue();
        result.IsMixedSelection.Should().BeFalse();

        // Must include batch resize, but exclude non-batch crop
        var editOps = result.CategorizedOperations[OperationCategory.Edit];
        editOps.Select(o => o.Id).Should().Contain("image-batch-resize");
        editOps.Select(o => o.Id).Should().NotContain("image-crop-single");
    }

    [Fact]
    public void Resolve_MixedSelection_ShouldOnlyIncludeUniversalBatchOperations()
    {
        var file1 = MakeFile("photo.png", ".png", FileFamily.Image);
        var file2 = MakeFile("report.pdf", ".pdf", FileFamily.Document);
        var file3 = MakeFile("archive.zip", ".zip", FileFamily.Archive);

        var result = _resolver.Resolve([file1, file2, file3]);

        result.IsBatchOperation.Should().BeTrue();
        result.IsMixedSelection.Should().BeTrue();

        // Only wildcard ops like universal-zip and universal-hash should apply to all three
        result.AvailableCategories.Should().Contain([OperationCategory.Create, OperationCategory.Analyze]);
        result.AvailableCategories.Should().NotContain(OperationCategory.Convert);
        result.AvailableCategories.Should().NotContain(OperationCategory.Organize);

        result.CategorizedOperations[OperationCategory.Create].Select(o => o.Id).Should().Contain("universal-zip");
        result.CategorizedOperations[OperationCategory.Analyze].Select(o => o.Id).Should().Contain("universal-hash");
    }

    [Fact]
    public void ResolveForCategory_ShouldReturnOnlyRequestedCategory()
    {
        var file = MakeFile("photo.png", ".png", FileFamily.Image);

        var result = _resolver.ResolveForCategory([file], OperationCategory.Convert);

        result.AvailableCategories.Should().ContainSingle().Which.Should().Be(OperationCategory.Convert);
        result.CategorizedOperations.Should().ContainKey(OperationCategory.Convert);
        result.CategorizedOperations.Should().NotContainKey(OperationCategory.Edit);
    }
}
