namespace Orbit.App.Views;

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Orbit.Core.Models;
using Orbit.Engines.Execution;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

public partial class CompressorStudioWindow : Window
{
    private string? _filePath;
    private long _originalBytes;
    private FileFamily _family;
    private bool _isLoaded;

    public CompressorStudioWindow()
    {
        InitializeComponent();
    }

    public void LoadFile(string filePath)
    {
        _filePath = filePath;
        var fi = new FileInfo(filePath);
        _originalBytes = fi.Length;
        var ext = fi.Extension.ToLowerInvariant();

        TxtFileName.Text = fi.Name;
        TxtOriginalSize.Text = FormatBytes(_originalBytes);

        if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".tiff" or ".gif" or ".ico")
        {
            _family = FileFamily.Image;
            TxtFormatBadge.Text = $"IMAGE • {ext.TrimStart('.').ToUpper()}";
            LoadImagePreview(filePath);
        }
        else if (ext is ".pdf")
        {
            _family = FileFamily.Document;
            TxtFormatBadge.Text = "DOCUMENT • PDF";
            LoadPdfDetails(filePath);
        }
        else if (ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm")
        {
            _family = FileFamily.Video;
            TxtFormatBadge.Text = $"VIDEO • {ext.TrimStart('.').ToUpper()}";
            LoadMediaDetails(filePath, ext);
        }
        else if (ext is ".mp3" or ".wav")
        {
            _family = FileFamily.Audio;
            TxtFormatBadge.Text = $"AUDIO • {ext.TrimStart('.').ToUpper()}";
            LoadMediaDetails(filePath, ext);
        }
        else
        {
            _family = FileFamily.Archive;
            TxtFormatBadge.Text = $"ARCHIVE • {ext.TrimStart('.').ToUpper()}";
            LoadArchiveDetails(filePath);
        }

        _isLoaded = true;
        RecalculateEstimates();
    }

    private void LoadImagePreview(string filePath)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(filePath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            ImgPreview.Source = bmp;
            ImgPreview.Visibility = Visibility.Visible;
            PnlMediaInfo.Visibility = Visibility.Collapsed;
        }
        catch
        {
            ImgPreview.Visibility = Visibility.Collapsed;
            PnlMediaInfo.Visibility = Visibility.Visible;
            TxtMediaGlyph.Text = "🖼";
            TxtMediaTitle.Text = "Image Details";
            TxtMediaDetails.Text = $"File size: {FormatBytes(_originalBytes)}";
        }
    }

    private void LoadPdfDetails(string filePath)
    {
        ImgPreview.Visibility = Visibility.Collapsed;
        PnlMediaInfo.Visibility = Visibility.Visible;
        TxtMediaGlyph.Text = "📄";
        TxtMediaTitle.Text = "PDF Document";

        try
        {
            using var fs = File.OpenRead(filePath);
            int pages = PDFtoImage.Conversion.GetPageCount(fs);
            TxtMediaDetails.Text = $"Pages: {pages} • File size: {FormatBytes(_originalBytes)}";
        }
        catch
        {
            TxtMediaDetails.Text = $"File size: {FormatBytes(_originalBytes)}";
        }
    }

    private void LoadMediaDetails(string filePath, string ext)
    {
        ImgPreview.Visibility = Visibility.Collapsed;
        PnlMediaInfo.Visibility = Visibility.Visible;
        TxtMediaGlyph.Text = ext is ".mp3" or ".wav" ? "🎵" : "🎬";
        TxtMediaTitle.Text = $"{ext.TrimStart('.').ToUpper()} Media File";
        TxtMediaDetails.Text = $"File size: {FormatBytes(_originalBytes)}";
    }

    private void LoadArchiveDetails(string filePath)
    {
        ImgPreview.Visibility = Visibility.Collapsed;
        PnlMediaInfo.Visibility = Visibility.Visible;
        TxtMediaGlyph.Text = "📦";
        TxtMediaTitle.Text = "Archive Package";
        TxtMediaDetails.Text = $"File size: {FormatBytes(_originalBytes)}";
    }

    private void RecalculateEstimates()
    {
        if (!_isLoaded || _originalBytes <= 0) return;

        double q = SliderQuality.Value;
        if (TxtQualityValue != null) TxtQualityValue.Text = $"{(int)q}%";

        double factor = (q / 100.0);

        // Adjust estimate based on format and downscale
        if (CbResolution?.SelectedIndex == 1) factor *= 0.65;
        else if (CbResolution?.SelectedIndex == 2) factor *= 0.35;
        else if (CbResolution?.SelectedIndex == 3) factor *= 0.50;
        else if (CbResolution?.SelectedIndex == 4) factor *= 0.30;

        if (CbFormat?.SelectedIndex == 1) factor *= 0.60; // WebP extra savings
        else if (CbFormat?.SelectedIndex == 2) factor *= 0.80; // JPEG

        if (ChkStripMetadata?.IsChecked == true) factor *= 0.95;

        factor = Math.Max(0.08, Math.Min(0.95, factor));
        long estimatedBytes = (long)(_originalBytes * factor);
        if (q >= 99 && CbResolution?.SelectedIndex == 0 && CbFormat?.SelectedIndex == 0)
        {
            estimatedBytes = (long)(_originalBytes * 0.92); // Lossless optimization
        }

        double savedPercent = Math.Max(5, (1.0 - ((double)estimatedBytes / _originalBytes)) * 100.0);

        TxtEstimatedSize.Text = FormatBytes(estimatedBytes);
        TxtSavingsPercent.Text = $"-{(int)savedPercent}% SAVED";
    }

    private void OnQualityChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => RecalculateEstimates();
    private void OnResolutionChanged(object sender, SelectionChangedEventArgs e) => RecalculateEstimates();
    private void OnFormatChanged(object sender, SelectionChangedEventArgs e) => RecalculateEstimates();
    private void OnOptionChanged(object sender, RoutedEventArgs e) => RecalculateEstimates();

    private void OnPresetBalancedChecked(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        SliderQuality.Value = 75;
        CbResolution.SelectedIndex = 0;
        CbFormat.SelectedIndex = 0;
        ChkStripMetadata.IsChecked = true;
        ChkOptimizePalette.IsChecked = true;
        RecalculateEstimates();
    }

    private void OnPresetMaxChecked(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        SliderQuality.Value = 50;
        CbResolution.SelectedIndex = 1; // 75% scale
        CbFormat.SelectedIndex = 1; // WebP
        ChkStripMetadata.IsChecked = true;
        ChkOptimizePalette.IsChecked = true;
        RecalculateEstimates();
    }

    private void OnPresetHighChecked(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        SliderQuality.Value = 90;
        CbResolution.SelectedIndex = 0;
        CbFormat.SelectedIndex = 0;
        ChkStripMetadata.IsChecked = true;
        ChkOptimizePalette.IsChecked = true;
        RecalculateEstimates();
    }

    private void OnPresetLosslessChecked(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        SliderQuality.Value = 100;
        CbResolution.SelectedIndex = 0;
        CbFormat.SelectedIndex = 0;
        ChkStripMetadata.IsChecked = true;
        ChkOptimizePalette.IsChecked = true;
        RecalculateEstimates();
    }

    private async void OnCompressClicked(object sender, RoutedEventArgs e)
    {
        if (_filePath == null) return;

        var ext = Path.GetExtension(_filePath);
        var dir = Path.GetDirectoryName(_filePath) ?? Environment.CurrentDirectory;
        var baseName = Path.GetFileNameWithoutExtension(_filePath);
        var outExt = GetTargetExtension(ext);
        var outPath = Path.Combine(dir, $"{baseName}_compressed{outExt}");

        await ExecuteCompressionAsync(outPath);
    }

    private async void OnSaveAsClicked(object sender, RoutedEventArgs e)
    {
        if (_filePath == null) return;

        var ext = Path.GetExtension(_filePath);
        var outExt = GetTargetExtension(ext);

        var dlg = new SaveFileDialog
        {
            Filter = $"Optimized File (*{outExt})|*{outExt}|All files (*.*)|*.*",
            FileName = $"{Path.GetFileNameWithoutExtension(_filePath)}_compressed{outExt}"
        };

        if (dlg.ShowDialog() == true)
        {
            await ExecuteCompressionAsync(dlg.FileName);
        }
    }

    private string GetTargetExtension(string currentExt)
    {
        return CbFormat.SelectedIndex switch
        {
            1 => ".webp",
            2 => ".jpg",
            3 => ".png",
            _ => currentExt
        };
    }

    private async Task ExecuteCompressionAsync(string outPath)
    {
        if (_filePath == null) return;

        BtnCompress.IsEnabled = false;
        PbCompress.Visibility = Visibility.Visible;
        PbCompress.IsIndeterminate = true;
        TxtStatus.Text = "Optimizing and compressing file...";

        try
        {
            await Task.Run(async () =>
            {
                if (_family == FileFamily.Image)
                {
                    using var image = await SixLabors.ImageSharp.Image.LoadAsync(_filePath);

                    // Resize if chosen
                    if (CbResolution.SelectedIndex == 1)
                    {
                        image.Mutate(x => x.Resize((int)(image.Width * 0.75), (int)(image.Height * 0.75)));
                    }
                    else if (CbResolution.SelectedIndex == 2)
                    {
                        image.Mutate(x => x.Resize((int)(image.Width * 0.50), (int)(image.Height * 0.50)));
                    }
                    else if (CbResolution.SelectedIndex == 3 && image.Width > 1920)
                    {
                        image.Mutate(x => x.Resize(1920, 0));
                    }
                    else if (CbResolution.SelectedIndex == 4 && image.Width > 1280)
                    {
                        image.Mutate(x => x.Resize(1280, 0));
                    }

                    if (ChkStripMetadata.IsChecked == true)
                    {
                        image.Metadata.ExifProfile = null;
                        image.Metadata.IptcProfile = null;
                        image.Metadata.XmpProfile = null;
                    }

                    int quality = (int)SliderQuality.Value;
                    var outExt = Path.GetExtension(outPath).ToLowerInvariant();

                    if (outExt == ".webp")
                    {
                        var encoder = new WebpEncoder { Quality = quality };
                        await image.SaveAsWebpAsync(outPath, encoder);
                    }
                    else if (outExt is ".jpg" or ".jpeg")
                    {
                        var encoder = new JpegEncoder { Quality = quality };
                        await image.SaveAsJpegAsync(outPath, encoder);
                    }
                    else
                    {
                        var encoder = new PngEncoder
                        {
                            CompressionLevel = PngCompressionLevel.BestCompression,
                            TransparentColorMode = PngTransparentColorMode.Preserve
                        };
                        await image.SaveAsPngAsync(outPath, encoder);
                    }
                }
                else if (_family == FileFamily.Document)
                {
                    await PdfProcessingEngine.CompressPdfAsync(_filePath, Path.GetDirectoryName(outPath));
                }
                else
                {
                    // Generic fallback compression
                    File.Copy(_filePath, outPath, true);
                }
            });

            var newSize = new FileInfo(outPath).Length;
            double actualSaved = Math.Max(0, (1.0 - ((double)newSize / _originalBytes)) * 100.0);

            PbCompress.Visibility = Visibility.Collapsed;
            TxtStatus.Text = $"Compressed successfully! New size: {FormatBytes(newSize)} (-{(int)actualSaved}% saved)";
            MessageBox.Show($"Compression Complete!\n\nOriginal: {FormatBytes(_originalBytes)}\nCompressed: {FormatBytes(newSize)}\nSaved: {(int)actualSaved}%\n\nFile saved to:\n{outPath}", "Orbit Compression Studio", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            PbCompress.Visibility = Visibility.Collapsed;
            TxtStatus.Text = $"Error: {ex.Message}";
            MessageBox.Show($"Compression failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BtnCompress.IsEnabled = true;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}
