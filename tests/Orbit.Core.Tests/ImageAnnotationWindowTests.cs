namespace Orbit.Core.Tests;

using System;
using System.IO;
using System.Threading;
using FluentAssertions;
using Orbit.App.Views;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

public class ImageAnnotationWindowTests : IDisposable
{
    private readonly string _tempDir;

    public ImageAnnotationWindowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Orbit_UI_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    private static void EnsureApplicationResources()
    {
        var app = System.Windows.Application.Current ?? new System.Windows.Application();
        var brushes = new (string Key, System.Windows.Media.Color Color)[]
        {
            ("OrbitBackgroundBrush", System.Windows.Media.Color.FromRgb(0x18, 0x19, 0x20)),
            ("OrbitSurfaceBrush", System.Windows.Media.Color.FromRgb(0x23, 0x25, 0x30)),
            ("OrbitBorderBrush", System.Windows.Media.Color.FromRgb(0x35, 0x37, 0x45)),
            ("OrbitAccentBrush", System.Windows.Media.Color.FromRgb(0xFF, 0x7A, 0x1A)),
            ("OrbitTextPrimaryBrush", System.Windows.Media.Color.FromRgb(0xF8, 0xFA, 0xFC)),
            ("OrbitTextSecondaryBrush", System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8)),
            ("OrbitTextMutedBrush", System.Windows.Media.Color.FromRgb(0x64, 0x74, 0x8B)),
        };

        foreach (var (key, color) in brushes)
        {
            if (!app.Resources.Contains(key))
            {
                app.Resources[key] = new System.Windows.Media.SolidColorBrush(color);
            }
        }
    }

    [Fact]
    public void ImageAnnotationWindow_ShouldInstantiateAndSetCropToolWithoutNullReference()
    {
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();

                // 1. Create a dummy image file
                var imgPath = Path.Combine(_tempDir, "test_ui.png");
                using (var img = new Image<Rgba32>(100, 100))
                {
                    img.SaveAsPng(imgPath);
                }

                // 2. Instantiate ImageAnnotationWindow
                var window = new ImageAnnotationWindow();
                window.Should().NotBeNull();

                // 3. Load the image
                window.LoadImage(imgPath);

                // 4. Set tool to Crop
                window.SetTool(ImageAnnotationWindow.AnnotationTool.Crop);

                // 5. Clean close
                window.Close();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(5));

        threadException.Should().BeNull("ImageAnnotationWindow should initialize cleanly without any NullReferenceException");
    }

    [Fact]
    public void ImageAnnotationWindow_ShouldSupportStudioModesAndTools()
    {
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();

                var imgPath = Path.Combine(_tempDir, "studio_test.png");
                using (var img = new Image<Rgba32>(120, 80))
                {
                    img.SaveAsPng(imgPath);
                }

                var window = new ImageAnnotationWindow();
                window.LoadImage(imgPath);

                // Test modes
                window.SetMode(ImageAnnotationWindow.StudioMode.Markup, ImageAnnotationWindow.AnnotationTool.Pen);
                window.SetMode(ImageAnnotationWindow.StudioMode.Adjust);
                window.SetMode(ImageAnnotationWindow.StudioMode.Watermark);
                window.SetMode(ImageAnnotationWindow.StudioMode.Transform, ImageAnnotationWindow.AnnotationTool.Crop);

                // Test tools
                window.SetTool(ImageAnnotationWindow.AnnotationTool.Pen);
                window.SetTool(ImageAnnotationWindow.AnnotationTool.StepBadge);
                window.SetTool(ImageAnnotationWindow.AnnotationTool.Arrow);

                window.Close();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(5));

        threadException.Should().BeNull("Image Studio modes and tools should execute cleanly without error");
    }

    [Fact]
    public void RadialMenuControl_ShouldAutoSelectAndCommitSingleActionCategory()
    {
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();

                var menu = new Orbit.App.Controls.RadialMenuControl();
                var op = new Orbit.Core.Models.OperationDefinition
                {
                    Id = "image-editor",
                    Label = "Photo Studio",
                    Category = Orbit.Core.Models.OperationCategory.Edit,
                    RequiresWorkspace = true,
                    InputFormats = [".png"],
                    EngineId = "orbit.engine.image"
                };

                var caps = new Orbit.Core.Services.ResolvedCapabilities
                {
                    Files = [],
                    CategorizedOperations = new Dictionary<Orbit.Core.Models.OperationCategory, List<Orbit.Core.Models.OperationDefinition>>
                    {
                        [Orbit.Core.Models.OperationCategory.Edit] = new List<Orbit.Core.Models.OperationDefinition> { op }
                    },
                    AvailableCategories = [Orbit.Core.Models.OperationCategory.Edit],
                    IsBatchOperation = false,
                    IsMixedSelection = false
                };

                menu.Populate(caps);
                menu.SelectCategory(Orbit.Core.Models.OperationCategory.Edit);

                Orbit.Core.Models.OperationDefinition? committed = null;
                menu.ActionCommitted += (action) => committed = action;

                menu.CommitCurrentSelection();

                committed.Should().NotBeNull();
                committed!.Id.Should().Be("image-editor");
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(5));

        threadException.Should().BeNull("RadialMenuControl should auto-select and commit single action categories cleanly");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }
}
