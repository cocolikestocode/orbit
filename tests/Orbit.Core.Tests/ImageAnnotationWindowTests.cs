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

    [Fact]
    public void ImageAnnotationWindow_ShouldInstantiateAndSetCropToolWithoutNullReference()
    {
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            try
            {
                // Ensure WPF Application instance exists with merged theme resources
                if (System.Windows.Application.Current == null)
                {
                    var app = new System.Windows.Application();
                    try
                    {
                        var dict = new System.Windows.ResourceDictionary
                        {
                            Source = new Uri("pack://application:,,,/Orbit.App;component/Themes/DarkTheme.xaml", UriKind.Absolute)
                        };
                        app.Resources.MergedDictionaries.Add(dict);
                    }
                    catch
                    {
                        // Fallback manual palette
                        app.Resources["OrbitBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x18, 0x19, 0x20));
                        app.Resources["OrbitSurfaceBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x23, 0x25, 0x30));
                        app.Resources["OrbitBorderBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x35, 0x37, 0x45));
                        app.Resources["OrbitAccentBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x7A, 0x1A));
                        app.Resources["OrbitTextPrimaryBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0xFA, 0xFC));
                        app.Resources["OrbitTextSecondaryBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8));
                        app.Resources["OrbitTextMutedBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x64, 0x74, 0x8B));
                    }
                }

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
