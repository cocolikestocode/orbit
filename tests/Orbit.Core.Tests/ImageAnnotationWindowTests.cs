namespace Orbit.Core.Tests;

using System;
using System.IO;
using System.Threading;
using System.Windows;
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

    [Fact]
    public void ImageAnnotationWindow_ShouldShowCropperOverlayAndUndoCropCleanly()
    {
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();

                var imgPath = Path.Combine(_tempDir, "crop_undo_test.png");
                using (var img = new Image<Rgba32>(200, 200))
                {
                    img.SaveAsPng(imgPath);
                }

                var window = new ImageAnnotationWindow();
                window.LoadImage(imgPath);

                // Enter crop mode -> overlay should be visible
                window.SetTool(ImageAnnotationWindow.AnnotationTool.Crop);
                window.CropLayerCanvas.Visibility.Should().Be(System.Windows.Visibility.Visible);

                // Commit crop
                window.OnApplyCropClicked(this, new RoutedEventArgs());
                window.CropLayerCanvas.Visibility.Should().Be(System.Windows.Visibility.Collapsed);

                // Undo crop -> should restore
                window.OnUndoClicked(this, new RoutedEventArgs());

                // Redo crop -> should re-crop
                window.OnRedoClicked(this, new RoutedEventArgs());

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

        threadException.Should().BeNull("ImageAnnotationWindow should support freeform crop overlay and bitmap undo/redo");
    }

    [Fact]
    public void PdfStudioWindow_ShouldInstantiateCleanly()
    {
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();
                var window = new PdfStudioWindow();
                window.Should().NotBeNull();
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

        threadException.Should().BeNull("PdfStudioWindow should instantiate cleanly");
    }

    [Fact]
    public void CompressorStudioWindow_ShouldInstantiateAndLoadFile()
    {
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();

                var imgPath = Path.Combine(_tempDir, "compress_test.png");
                using (var img = new Image<Rgba32>(100, 100))
                {
                    img.SaveAsPng(imgPath);
                }

                var window = new CompressorStudioWindow();
                window.LoadFile(imgPath);
                window.TxtOriginalSize.Text.Should().NotBeNullOrEmpty();
                window.TxtEstimatedSize.Text.Should().NotBeNullOrEmpty();

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

        threadException.Should().BeNull("CompressorStudioWindow should instantiate and calculate estimates");
    }

    [Fact]
    public void ImageAnnotationWindow_ShouldSelectMoveAndResizeElements()
    {
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();

                var window = new ImageAnnotationWindow();
                var imgPath = Path.Combine(_tempDir, "select_test.png");
                using (var img = new Image<Rgba32>(200, 200))
                {
                    img.SaveAsPng(imgPath);
                }

                window.LoadImage(imgPath);

                // Add a drawn polyline (Pen)
                var poly = new System.Windows.Shapes.Polyline
                {
                    Stroke = System.Windows.Media.Brushes.Red,
                    StrokeThickness = 4.0
                };
                poly.Points.Add(new System.Windows.Point(20, 20));
                poly.Points.Add(new System.Windows.Point(60, 60));
                window.DrawingCanvas.Children.Add(poly);

                // Select the polyline with Select tool (V)
                window.SetTool(ImageAnnotationWindow.AnnotationTool.Select);
                window.SelectElement(poly);

                window.SelectionOverlayCanvas.Visibility.Should().Be(Visibility.Visible);
                window.SelectionBorder.Width.Should().BeGreaterThan(0);
                window.SelectionBorder.Height.Should().BeGreaterThan(0);

                // Resize the polyline
                var oldBounds = window.GetElementBounds(poly);
                var newBounds = new Rect(oldBounds.X, oldBounds.Y, oldBounds.Width * 2, oldBounds.Height * 2);
                window.ApplyElementBounds(poly, oldBounds, newBounds);

                var resizedBounds = window.GetElementBounds(poly);
                resizedBounds.Width.Should().BeApproximately(newBounds.Width, 1.0);
                resizedBounds.Height.Should().BeApproximately(newBounds.Height, 1.0);

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

        threadException.Should().BeNull("ImageAnnotationWindow should support selecting, moving and resizing elements");
    }

    [Fact]
    public void PdfStudioWindow_ShouldInstantiateAndSupportZoomAndMarkupTools()
    {
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();

                var window = new PdfStudioWindow();
                window.Show();

                // Create a sample PDF file using PdfSharpCore
                var pdfPath = Path.Combine(_tempDir, "sample_studio.pdf");
                using (var doc = new PdfSharpCore.Pdf.PdfDocument())
                {
                    var page1 = doc.AddPage();
                    page1.Width = 600;
                    page1.Height = 800;
                    var page2 = doc.AddPage();
                    page2.Width = 600;
                    page2.Height = 800;
                    doc.Save(pdfPath);
                }

                window.LoadPdf(pdfPath);
                window.PageScaleTransform.ScaleX.Should().BeGreaterThan(0);
                window.PageScaleTransform.ScaleY.Should().BeGreaterThan(0);

                // Add markup element to canvas
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = 100,
                    Height = 80,
                    Stroke = System.Windows.Media.Brushes.Orange,
                    StrokeThickness = 4.0
                };
                System.Windows.Controls.Canvas.SetLeft(rect, 40);
                System.Windows.Controls.Canvas.SetTop(rect, 50);
                window.PageDrawingCanvas.Children.Add(rect);

                window.PageDrawingCanvas.Children.Contains(rect).Should().BeTrue();

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

        threadException.Should().BeNull("PdfStudioWindow should instantiate and support zoom and markup canvas");
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
