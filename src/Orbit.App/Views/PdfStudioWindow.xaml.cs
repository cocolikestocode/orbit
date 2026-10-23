namespace Orbit.App.Views;

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

public partial class PdfStudioWindow : Window
{
    public class PageItem : INotifyPropertyChanged
    {
        public int PageIndex { get; set; }
        public string PageLabel => $"Page {PageIndex + 1}";
        public BitmapImage? Image { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        public void NotifyChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private string? _currentPdfPath;
    private PdfDocument? _document;
    private readonly ObservableCollection<PageItem> _pages = new();
    private bool _isDirty;
    private double _zoom = 1.0;

    public PdfStudioWindow()
    {
        InitializeComponent();
        LbThumbnails.ItemsSource = _pages;
        Closing += OnWindowClosing;
    }

    public void LoadPdf(string pdfPath)
    {
        try
        {
            _currentPdfPath = pdfPath;
            _document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);

            Title = $"Orbit — PDF Studio • {Path.GetFileName(pdfPath)}";
            TxtDocMeta.Text = $"{Path.GetFileName(pdfPath)} • {_document.PageCount} pages • {new FileInfo(pdfPath).Length / 1024:N0} KB";
            TxtPagesHeader.Text = $"Pages ({_document.PageCount})";

            RenderAllPages();
            SetDirty(false);
            TxtStatus.Text = $"Loaded {Path.GetFileName(pdfPath)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load PDF: {ex.Message}", "Orbit PDF Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RenderAllPages()
    {
        _pages.Clear();
        if (_document == null || _currentPdfPath == null) return;

        // Render high-res previews using PDFtoImage
        try
        {
            var pdfBytes = File.ReadAllBytes(_currentPdfPath);
            int count = PDFtoImage.Conversion.GetPageCount(pdfBytes);

            for (int i = 0; i < count; i++)
            {
                using var ms = new MemoryStream();
                PDFtoImage.Conversion.SavePng(ms, pdfBytes, page: i, options: new PDFtoImage.RenderOptions(150));
                ms.Position = 0;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();

                _pages.Add(new PageItem
                {
                    PageIndex = i,
                    Image = bmp
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[PDF Studio] Error rendering previews: {ex.Message}");
            // Fallback lightweight page indicators if rendering stream had issues
            for (int i = 0; i < _document.PageCount; i++)
            {
                _pages.Add(new PageItem { PageIndex = i });
            }
        }

        if (_pages.Count > 0)
        {
            LbThumbnails.SelectedIndex = -1;
            LbThumbnails.SelectedIndex = 0;
            if (_pages[0].Image != null)
            {
                ActivePageImage.Source = _pages[0].Image;
                ApplyZoom();
                TxtCurrentPageInfo.Text = $"Selected: Page 1 of {_pages.Count}";
            }
        }
    }

    private void OnThumbnailSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LbThumbnails.SelectedItem is PageItem item && item.Image != null)
        {
            ActivePageImage.Source = item.Image;
            ApplyZoom();
            TxtCurrentPageInfo.Text = $"Selected: Page {item.PageIndex + 1} of {_pages.Count}";
        }
    }

    private void SetDirty(bool dirty)
    {
        _isDirty = dirty;
        BadgeDirty.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRotateLeftClicked(object sender, RoutedEventArgs e)
    {
        RotateSelectedPage(270);
    }

    private void OnRotateRightClicked(object sender, RoutedEventArgs e)
    {
        RotateSelectedPage(90);
    }

    private void RotateSelectedPage(int deg)
    {
        int idx = LbThumbnails.SelectedIndex;
        if (idx < 0 || _document == null || idx >= _document.PageCount) return;

        var page = _document.Pages[idx];
        page.Rotate = (page.Rotate + deg) % 360;
        SetDirty(true);
        TxtStatus.Text = $"Rotated Page {idx + 1} by {deg}°";
        RefreshCurrentPagePreview(idx, deg);
    }

    private void OnMoveUpClicked(object sender, RoutedEventArgs e)
    {
        int idx = LbThumbnails.SelectedIndex;
        if (idx <= 0 || _document == null) return;

        var page = _document.Pages[idx];
        _document.Pages.RemoveAt(idx);
        _document.Pages.Insert(idx - 1, page);

        var item = _pages[idx];
        _pages.RemoveAt(idx);
        _pages.Insert(idx - 1, item);

        RenumberPages();
        LbThumbnails.SelectedIndex = idx - 1;
        SetDirty(true);
        TxtStatus.Text = $"Moved page to position {idx}";
    }

    private void OnMoveDownClicked(object sender, RoutedEventArgs e)
    {
        int idx = LbThumbnails.SelectedIndex;
        if (idx < 0 || idx >= _pages.Count - 1 || _document == null) return;

        var page = _document.Pages[idx];
        _document.Pages.RemoveAt(idx);
        _document.Pages.Insert(idx + 1, page);

        var item = _pages[idx];
        _pages.RemoveAt(idx);
        _pages.Insert(idx + 1, item);

        RenumberPages();
        LbThumbnails.SelectedIndex = idx + 1;
        SetDirty(true);
        TxtStatus.Text = $"Moved page to position {idx + 2}";
    }

    private void OnDeletePageClicked(object sender, RoutedEventArgs e)
    {
        int idx = LbThumbnails.SelectedIndex;
        if (idx < 0 || _document == null || _document.PageCount <= 1)
        {
            MessageBox.Show("Cannot delete the only page in the document.", "PDF Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"Are you sure you want to delete Page {idx + 1}?", "Delete Page", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _document.Pages.RemoveAt(idx);
        _pages.RemoveAt(idx);
        RenumberPages();

        int newIdx = Math.Min(idx, _pages.Count - 1);
        LbThumbnails.SelectedIndex = newIdx;
        SetDirty(true);
        TxtPagesHeader.Text = $"Pages ({_document.PageCount})";
        TxtStatus.Text = $"Deleted page. {_document.PageCount} pages remaining.";
    }

    private void OnExtractPageClicked(object sender, RoutedEventArgs e)
    {
        int idx = LbThumbnails.SelectedIndex;
        if (idx < 0 || _document == null) return;

        var dlg = new SaveFileDialog
        {
            Filter = "PDF Document (*.pdf)|*.pdf",
            FileName = $"{Path.GetFileNameWithoutExtension(_currentPdfPath)}_page_{idx + 1}.pdf"
        };

        if (dlg.ShowDialog() == true)
        {
            using var singleDoc = new PdfDocument();
            singleDoc.AddPage(_document.Pages[idx]);
            singleDoc.Save(dlg.FileName);
            TxtStatus.Text = $"Extracted page saved to: {Path.GetFileName(dlg.FileName)}";
        }
    }

    private void OnProtectClicked(object sender, RoutedEventArgs e)
    {
        var (confirmed, password) = PasswordInputDialog.Prompt("Protect PDF Document", "Enter a password to encrypt this document.");
        if (confirmed && !string.IsNullOrEmpty(password) && _document != null)
        {
            _document.SecuritySettings.UserPassword = password;
            _document.SecuritySettings.OwnerPassword = password + "_admin";
            SetDirty(true);
            TxtStatus.Text = "Document password protection armed. Click Save to commit.";
            MessageBox.Show("Password protection set! Save the document to apply encryption.", "Orbit Security", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void OnExportImagesClicked(object sender, RoutedEventArgs e)
    {
        if (_currentPdfPath == null) return;

        try
        {
            TxtStatus.Text = "Exporting pages as high-resolution images...";
            var targetDir = Path.Combine(Path.GetDirectoryName(_currentPdfPath) ?? Environment.CurrentDirectory, $"{Path.GetFileNameWithoutExtension(_currentPdfPath)}_pages");
            Directory.CreateDirectory(targetDir);

            var images = await Orbit.Engines.Execution.PdfProcessingEngine.RenderPdfToImagesAsync(_currentPdfPath, targetDir);
            TxtStatus.Text = $"Exported {images.Count} page images successfully to {Path.GetFileName(targetDir)}!";
            MessageBox.Show($"Exported {images.Count} page images to:\n{targetDir}", "Orbit PDF Studio", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export images: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (_document == null || _currentPdfPath == null) return;

        try
        {
            _document.Save(_currentPdfPath);
            SetDirty(false);
            TxtStatus.Text = "Saved PDF document successfully.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save directly to file: {ex.Message}\nTry 'Save As...'", "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnSaveAsClicked(object sender, RoutedEventArgs e)
    {
        if (_document == null || _currentPdfPath == null) return;

        var dlg = new SaveFileDialog
        {
            Filter = "PDF Document (*.pdf)|*.pdf",
            FileName = $"{Path.GetFileNameWithoutExtension(_currentPdfPath)}_edited.pdf"
        };

        if (dlg.ShowDialog() == true)
        {
            _document.Save(dlg.FileName);
            SetDirty(false);
            TxtStatus.Text = $"Saved as: {Path.GetFileName(dlg.FileName)}";
        }
    }

    private void RenumberPages()
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].PageIndex = i;
            _pages[i].NotifyChanged();
        }
    }

    private void RefreshCurrentPagePreview(int idx, int deg = 90)
    {
        if (ActivePageImage.Source is BitmapSource bs)
        {
            var rotated = new TransformedBitmap(bs, new System.Windows.Media.RotateTransform(deg));
            rotated.Freeze();
            ActivePageImage.Source = rotated;
            if (idx >= 0 && idx < _pages.Count)
            {
                var bmpImg = new BitmapImage();
                using var ms = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rotated));
                encoder.Save(ms);
                ms.Position = 0;
                bmpImg.BeginInit();
                bmpImg.CacheOption = BitmapCacheOption.OnLoad;
                bmpImg.StreamSource = ms;
                bmpImg.EndInit();
                bmpImg.Freeze();
                _pages[idx].Image = bmpImg;
                _pages[idx].NotifyChanged();
            }
        }
    }

    private void OnZoomInClicked(object sender, RoutedEventArgs e)
    {
        _zoom = Math.Min(3.0, _zoom + 0.15);
        ApplyZoom();
    }

    private void OnZoomOutClicked(object sender, RoutedEventArgs e)
    {
        _zoom = Math.Max(0.25, _zoom - 0.15);
        ApplyZoom();
    }

    private void OnFitPageClicked(object sender, RoutedEventArgs e)
    {
        _zoom = 1.0;
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        if (ActivePageImage.Source is BitmapSource bs)
        {
            ActivePageImage.Width = bs.PixelWidth * _zoom;
            ActivePageImage.Height = bs.PixelHeight * _zoom;
            TxtZoomLevel.Text = $"{(int)(_zoom * 100)}%";
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_isDirty) return;

        var res = MessageBox.Show("You have unsaved changes in this PDF document. Save before closing?", "Orbit PDF Studio", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (res == MessageBoxResult.Yes)
        {
            OnSaveClicked(this, new RoutedEventArgs());
        }
        else if (res == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
        }
    }
}
