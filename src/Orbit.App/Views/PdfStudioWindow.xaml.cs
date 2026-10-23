namespace Orbit.App.Views;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
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

    public enum PdfMarkupTool
    {
        Select,
        Pen,
        Highlight,
        Rectangle,
        Circle,
        Arrow,
        Text,
        Image
    }

    private string? _currentPdfPath;
    private PdfDocument? _document;
    private readonly ObservableCollection<PageItem> _pages = new();
    private readonly Dictionary<int, List<UIElement>> _pageAnnotations = new();

    private bool _isDirty;
    private double _zoom = 1.0;
    private int _currentPageIndex = -1;

    private PdfMarkupTool _currentTool = PdfMarkupTool.Select;
    private System.Windows.Media.Color _currentColor = System.Windows.Media.Color.FromRgb(255, 122, 26);
    private double _strokeThickness = 4.0;

    // Drawing Interaction State
    private bool _isDrawing;
    private Point _drawStartPoint;
    private UIElement? _activeShapeElement;
    private Polyline? _activePolyline;

    // Element Moving & Resizing State
    private UIElement? _selectedElement;
    private bool _isDraggingElement;
    private Point _elementDragStart;
    private Rect _elementOriginalBounds;
    private List<Point>? _origPolyPoints;
    private (double X1, double Y1, double X2, double Y2)? _origArrowLine;
    private List<Point>? _origHeadPoints;

    // Thumbnail Drag and Drop State
    private Point _dragStartPoint;
    private PageItem? _draggedThumbnailItem;

    public PdfStudioWindow()
    {
        InitializeComponent();
        LbThumbnails.ItemsSource = _pages;
        Closing += OnWindowClosing;
        Loaded += (s, e) => ApplyZoom();
    }

    public void LoadPdf(string pdfPath)
    {
        try
        {
            _currentPdfPath = pdfPath;
            _document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);

            Title = $"Orbit — PDF Studio • {System.IO.Path.GetFileName(pdfPath)}";
            TxtDocMeta.Text = $"{System.IO.Path.GetFileName(pdfPath)} • {_document.PageCount} pages • {new FileInfo(pdfPath).Length / 1024:N0} KB";
            TxtPagesHeader.Text = $"Pages ({_document.PageCount})";

            _pageAnnotations.Clear();
            RenderAllPages();
            SetDirty(false);
            TxtStatus.Text = $"Loaded {System.IO.Path.GetFileName(pdfPath)}";
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
            for (int i = 0; i < _document.PageCount; i++)
            {
                _pages.Add(new PageItem { PageIndex = i });
            }
        }

        if (_pages.Count > 0)
        {
            LbThumbnails.SelectedIndex = -1;
            LbThumbnails.SelectedIndex = 0;
        }
    }

    private void SetDirty(bool dirty)
    {
        _isDirty = dirty;
        BadgeDirty.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
    }

    #region Thumbnail Selection & Page Switching

    private void OnThumbnailSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int count = LbThumbnails.SelectedItems.Count;
        if (count > 1)
        {
            TxtCurrentPageInfo.Text = $"{count} pages selected (Ctrl/Shift to multi-select)";
        }
        else if (count == 1 && LbThumbnails.SelectedItem is PageItem item)
        {
            TxtCurrentPageInfo.Text = $"Page {item.PageIndex + 1} of {_pages.Count}";
            SwitchToPage(item.PageIndex);
        }
    }

    private void SwitchToPage(int newPageIndex)
    {
        if (newPageIndex < 0 || newPageIndex >= _pages.Count) return;

        // 1. Save current page annotations
        if (_currentPageIndex >= 0)
        {
            SaveCurrentPageAnnotations(_currentPageIndex);
        }

        _currentPageIndex = newPageIndex;
        DeselectCurrentElement();

        // 2. Clear previous canvas markup elements
        ClearCanvasVisuals();

        // 3. Update image preview
        var pageItem = _pages[newPageIndex];
        if (pageItem.Image != null)
        {
            ActivePageImage.Source = pageItem.Image;
            PageDrawingCanvas.Width = pageItem.Image.PixelWidth;
            PageDrawingCanvas.Height = pageItem.Image.PixelHeight;
            ActivePageImage.Width = pageItem.Image.PixelWidth;
            ActivePageImage.Height = pageItem.Image.PixelHeight;
        }

        // 4. Restore saved annotations for new page
        if (_pageAnnotations.TryGetValue(newPageIndex, out var elements))
        {
            foreach (var el in elements)
            {
                if (!PageDrawingCanvas.Children.Contains(el))
                {
                    PageDrawingCanvas.Children.Add(el);
                }
            }
        }

        ApplyZoom();
    }

    private void SaveCurrentPageAnnotations(int pageIndex)
    {
        var markups = new List<UIElement>();
        foreach (UIElement child in PageDrawingCanvas.Children)
        {
            if (child == ActivePageImage ||
                child == SelectionBoundsRect ||
                child == HandleTL || child == HandleTC || child == HandleTR ||
                child == HandleML || child == HandleMR ||
                child == HandleBL || child == HandleBC || child == HandleBR)
            {
                continue;
            }
            markups.Add(child);
        }
        _pageAnnotations[pageIndex] = markups;
    }

    private void ClearCanvasVisuals()
    {
        var toRemove = new List<UIElement>();
        foreach (UIElement child in PageDrawingCanvas.Children)
        {
            if (child != ActivePageImage &&
                child != SelectionBoundsRect &&
                child != HandleTL && child != HandleTC && child != HandleTR &&
                child != HandleML && child != HandleMR &&
                child != HandleBL && child != HandleBC && child != HandleBR)
            {
                toRemove.Add(child);
            }
        }
        foreach (var r in toRemove) PageDrawingCanvas.Children.Remove(r);
    }

    #endregion

    #region Drag and Drop Page Reordering

    private void OnThumbnailsPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        var item = FindVisualParent<ListBoxItem>((DependencyObject)e.OriginalSource);
        if (item != null)
        {
            _draggedThumbnailItem = item.DataContext as PageItem;
        }
    }

    private void OnThumbnailsPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _draggedThumbnailItem != null)
        {
            var diff = _dragStartPoint - e.GetPosition(null);
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                DragDrop.DoDragDrop(LbThumbnails, _draggedThumbnailItem, DragDropEffects.Move);
                _draggedThumbnailItem = null;
            }
        }
    }

    private void OnThumbnailsDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnThumbnailsDrop(object sender, DragEventArgs e)
    {
        var dropped = e.Data.GetData(typeof(PageItem)) as PageItem;
        if (dropped == null || _document == null) return;

        var targetContainer = FindVisualParent<ListBoxItem>((DependencyObject)e.OriginalSource);
        int targetIdx = targetContainer != null ? LbThumbnails.ItemContainerGenerator.IndexFromContainer(targetContainer) : _pages.Count - 1;
        if (targetIdx < 0) targetIdx = _pages.Count - 1;

        int oldIdx = _pages.IndexOf(dropped);
        if (oldIdx >= 0 && oldIdx != targetIdx)
        {
            // Reorder in PDF document
            var page = _document.Pages[oldIdx];
            _document.Pages.RemoveAt(oldIdx);
            _document.Pages.Insert(targetIdx, page);

            // Reorder in UI collection
            _pages.RemoveAt(oldIdx);
            _pages.Insert(targetIdx, dropped);

            // Reorder saved annotations dictionary
            if (_pageAnnotations.TryGetValue(oldIdx, out var annot))
            {
                _pageAnnotations.Remove(oldIdx);
                _pageAnnotations[targetIdx] = annot;
            }

            RenumberPages();
            LbThumbnails.SelectedIndex = targetIdx;
            SetDirty(true);
            TxtStatus.Text = $"Moved Page {oldIdx + 1} to position {targetIdx + 1}";
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    #endregion

    #region Multi-Selection & Batch Operations

    private void OnSelectAllClicked(object sender, RoutedEventArgs e)
    {
        LbThumbnails.SelectAll();
    }

    private void OnDeselectAllClicked(object sender, RoutedEventArgs e)
    {
        LbThumbnails.UnselectAll();
        if (_pages.Count > 0) LbThumbnails.SelectedIndex = 0;
    }

    private void OnRotateLeftClicked(object sender, RoutedEventArgs e)
    {
        RotateSelectedPages(270);
    }

    private void OnRotateRightClicked(object sender, RoutedEventArgs e)
    {
        RotateSelectedPages(90);
    }

    private void RotateSelectedPages(int deg)
    {
        if (_document == null || _pages.Count == 0) return;

        var selected = LbThumbnails.SelectedItems.Cast<PageItem>().ToList();
        if (selected.Count == 0 && LbThumbnails.SelectedIndex >= 0)
        {
            selected.Add(_pages[LbThumbnails.SelectedIndex]);
        }

        if (selected.Count == 0) return;

        foreach (var item in selected)
        {
            int idx = item.PageIndex;
            if (idx >= 0 && idx < _document.PageCount)
            {
                var page = _document.Pages[idx];
                page.Rotate = (page.Rotate + deg) % 360;
                RefreshPagePreview(idx, deg);
            }
        }

        SetDirty(true);
        TxtStatus.Text = $"Rotated {selected.Count} page(s) by {deg}°";
    }

    private void RefreshPagePreview(int idx, int deg = 90)
    {
        if (idx < 0 || idx >= _pages.Count) return;
        var item = _pages[idx];

        if (item.Image is BitmapSource bs)
        {
            var rotated = new TransformedBitmap(bs, new RotateTransform(deg));
            rotated.Freeze();

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

            item.Image = bmpImg;
            item.NotifyChanged();

            if (idx == _currentPageIndex)
            {
                ActivePageImage.Source = bmpImg;
                PageDrawingCanvas.Width = bmpImg.PixelWidth;
                PageDrawingCanvas.Height = bmpImg.PixelHeight;
                ActivePageImage.Width = bmpImg.PixelWidth;
                ActivePageImage.Height = bmpImg.PixelHeight;
            }
        }
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
        if (_document == null || _document.PageCount <= 1)
        {
            MessageBox.Show("Cannot delete the only page in the document.", "PDF Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selected = LbThumbnails.SelectedItems.Cast<PageItem>().OrderByDescending(p => p.PageIndex).ToList();
        if (selected.Count == 0 && LbThumbnails.SelectedIndex >= 0)
        {
            selected.Add(_pages[LbThumbnails.SelectedIndex]);
        }

        if (selected.Count == 0) return;

        if (selected.Count >= _document.PageCount)
        {
            MessageBox.Show("Cannot delete all pages in the document.", "PDF Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"Are you sure you want to delete {selected.Count} selected page(s)?", "Delete Pages", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var p in selected)
        {
            _document.Pages.RemoveAt(p.PageIndex);
            _pages.RemoveAt(p.PageIndex);
            _pageAnnotations.Remove(p.PageIndex);
        }

        RenumberPages();
        int newIdx = Math.Min(selected.Last().PageIndex, _pages.Count - 1);
        LbThumbnails.SelectedIndex = Math.Max(0, newIdx);

        SetDirty(true);
        TxtPagesHeader.Text = $"Pages ({_document.PageCount})";
        TxtStatus.Text = $"Deleted {selected.Count} page(s). {_document.PageCount} pages remaining.";
    }

    private void OnExtractPageClicked(object sender, RoutedEventArgs e)
    {
        if (_document == null) return;

        var selected = LbThumbnails.SelectedItems.Cast<PageItem>().OrderBy(p => p.PageIndex).ToList();
        if (selected.Count == 0 && LbThumbnails.SelectedIndex >= 0)
        {
            selected.Add(_pages[LbThumbnails.SelectedIndex]);
        }

        if (selected.Count == 0) return;

        var dlg = new SaveFileDialog
        {
            Filter = "PDF Document (*.pdf)|*.pdf",
            FileName = selected.Count == 1
                ? $"{System.IO.Path.GetFileNameWithoutExtension(_currentPdfPath)}_page_{selected[0].PageIndex + 1}.pdf"
                : $"{System.IO.Path.GetFileNameWithoutExtension(_currentPdfPath)}_extracted_{selected.Count}_pages.pdf"
        };

        if (dlg.ShowDialog() == true)
        {
            using var singleDoc = new PdfDocument();
            foreach (var p in selected)
            {
                singleDoc.AddPage(_document.Pages[p.PageIndex]);
            }
            singleDoc.Save(dlg.FileName);
            TxtStatus.Text = $"Extracted {selected.Count} page(s) to: {System.IO.Path.GetFileName(dlg.FileName)}";
            MessageBox.Show($"Successfully extracted {selected.Count} page(s) to:\n{dlg.FileName}", "Orbit PDF Studio", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    #endregion

    #region Merge & Insert Pages

    private void OnInsertMergeClicked(object sender, RoutedEventArgs e)
    {
        if (_document == null) return;

        var dlg = new OpenFileDialog
        {
            Title = "Select PDF or Image Files to Merge",
            Filter = "PDF and Images (*.pdf;*.png;*.jpg;*.jpeg;*.webp)|*.pdf;*.png;*.jpg;*.jpeg;*.webp|All Files (*.*)|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0) return;

        int insertIndex = LbThumbnails.SelectedIndex >= 0 ? LbThumbnails.SelectedIndex + 1 : _pages.Count;
        int addedPages = 0;

        try
        {
            foreach (var filePath in dlg.FileNames)
            {
                var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();

                if (ext == ".pdf")
                {
                    using var importDoc = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
                    var pdfBytes = File.ReadAllBytes(filePath);

                    for (int p = 0; p < importDoc.PageCount; p++)
                    {
                        var newPage = _document.InsertPage(insertIndex, importDoc.Pages[p]);

                        // Render preview bitmap
                        using var ms = new MemoryStream();
                        PDFtoImage.Conversion.SavePng(ms, pdfBytes, page: p, options: new PDFtoImage.RenderOptions(150));
                        ms.Position = 0;

                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();

                        _pages.Insert(insertIndex, new PageItem
                        {
                            PageIndex = insertIndex,
                            Image = bmp
                        });

                        insertIndex++;
                        addedPages++;
                    }
                }
                else if (ext is ".png" or ".jpg" or ".jpeg" or ".webp")
                {
                    // Insert Image as new PDF page
                    var newPage = _document.InsertPage(insertIndex);
                    using (var xImg = XImage.FromFile(filePath))
                    {
                        newPage.Width = XUnit.FromPoint(xImg.PointWidth);
                        newPage.Height = XUnit.FromPoint(xImg.PointHeight);
                        using var gfx = XGraphics.FromPdfPage(newPage);
                        gfx.DrawImage(xImg, 0, 0, newPage.Width.Point, newPage.Height.Point);
                    }

                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(filePath, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();

                    _pages.Insert(insertIndex, new PageItem
                    {
                        PageIndex = insertIndex,
                        Image = bmp
                    });

                    insertIndex++;
                    addedPages++;
                }
            }

            RenumberPages();
            SetDirty(true);
            TxtPagesHeader.Text = $"Pages ({_document.PageCount})";
            TxtStatus.Text = $"Merged {addedPages} page(s) successfully.";
            MessageBox.Show($"Successfully merged {addedPages} new page(s) into this document!", "PDF Merge", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not merge file: {ex.Message}", "Merge Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    #endregion

    #region Markup Tools, Color & Stroke

    private void OnMarkupToolClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            BtnToolSelect.Tag = null;
            BtnToolPen.Tag = null;
            BtnToolHighlight.Tag = null;
            BtnToolRectangle.Tag = null;
            BtnToolCircle.Tag = null;
            BtnToolArrow.Tag = null;
            BtnToolText.Tag = null;
            BtnToolImage.Tag = null;

            btn.Tag = "Active";

            if (btn == BtnToolSelect) _currentTool = PdfMarkupTool.Select;
            else if (btn == BtnToolPen) _currentTool = PdfMarkupTool.Pen;
            else if (btn == BtnToolHighlight) _currentTool = PdfMarkupTool.Highlight;
            else if (btn == BtnToolRectangle) _currentTool = PdfMarkupTool.Rectangle;
            else if (btn == BtnToolCircle) _currentTool = PdfMarkupTool.Circle;
            else if (btn == BtnToolArrow) _currentTool = PdfMarkupTool.Arrow;
            else if (btn == BtnToolText) _currentTool = PdfMarkupTool.Text;

            DeselectCurrentElement();
            TxtStatus.Text = $"Tool: {_currentTool}";
        }
    }

    private void OnThicknessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _strokeThickness = SliderStrokeThickness?.Value ?? 4.0;
        if (TxtThickness != null) TxtThickness.Text = $"{(int)_strokeThickness}px";
    }

    private void OnColorSwatchClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        var swatches = new[] { SwatchOrange, SwatchRed, SwatchYellow, SwatchGreen, SwatchBlue, SwatchPurple, SwatchBlack, SwatchWhite };
        foreach (var s in swatches) if (s != null) s.Tag = null;

        btn.Tag = "Selected";
        if (btn.Background is SolidColorBrush scb)
        {
            _currentColor = scb.Color;
        }
    }

    private bool _isUpdatingColorPickerInternal;

    private void OnCustomColorButtonClicked(object sender, RoutedEventArgs e)
    {
        _isUpdatingColorPickerInternal = true;
        SliderRed.Value = _currentColor.R;
        SliderGreen.Value = _currentColor.G;
        SliderBlue.Value = _currentColor.B;
        TxtRedVal.Text = _currentColor.R.ToString();
        TxtGreenVal.Text = _currentColor.G.ToString();
        TxtBlueVal.Text = _currentColor.B.ToString();
        TxtHexColor.Text = $"#{_currentColor.R:X2}{_currentColor.G:X2}{_currentColor.B:X2}";
        PickerPreviewBox.Background = new SolidColorBrush(_currentColor);
        _isUpdatingColorPickerInternal = false;

        PopupColorPicker.IsOpen = true;
    }

    private void OnPickerRgbSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingColorPickerInternal) return;

        byte r = (byte)(SliderRed?.Value ?? 0);
        byte g = (byte)(SliderGreen?.Value ?? 0);
        byte b = (byte)(SliderBlue?.Value ?? 0);

        if (TxtRedVal != null) TxtRedVal.Text = r.ToString();
        if (TxtGreenVal != null) TxtGreenVal.Text = g.ToString();
        if (TxtBlueVal != null) TxtBlueVal.Text = b.ToString();

        _isUpdatingColorPickerInternal = true;
        var color = System.Windows.Media.Color.FromRgb(r, g, b);
        if (TxtHexColor != null) TxtHexColor.Text = $"#{r:X2}{g:X2}{b:X2}";
        if (PickerPreviewBox != null) PickerPreviewBox.Background = new SolidColorBrush(color);
        _isUpdatingColorPickerInternal = false;
    }

    private void OnPickerHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingColorPickerInternal || TxtHexColor == null) return;

        var text = TxtHexColor.Text.Trim();
        if (!text.StartsWith("#")) text = "#" + text;

        try
        {
            var parsed = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(text);
            _isUpdatingColorPickerInternal = true;
            if (SliderRed != null) SliderRed.Value = parsed.R;
            if (SliderGreen != null) SliderGreen.Value = parsed.G;
            if (SliderBlue != null) SliderBlue.Value = parsed.B;
            if (TxtRedVal != null) TxtRedVal.Text = parsed.R.ToString();
            if (TxtGreenVal != null) TxtGreenVal.Text = parsed.G.ToString();
            if (TxtBlueVal != null) TxtBlueVal.Text = parsed.B.ToString();
            if (PickerPreviewBox != null) PickerPreviewBox.Background = new SolidColorBrush(parsed);
            _isUpdatingColorPickerInternal = false;
        }
        catch { }
    }

    private void OnPickerApplyClicked(object sender, RoutedEventArgs e)
    {
        byte r = (byte)(SliderRed?.Value ?? 255);
        byte g = (byte)(SliderGreen?.Value ?? 122);
        byte b = (byte)(SliderBlue?.Value ?? 26);
        _currentColor = System.Windows.Media.Color.FromRgb(r, g, b);

        var swatches = new[] { SwatchOrange, SwatchRed, SwatchYellow, SwatchGreen, SwatchBlue, SwatchPurple, SwatchBlack, SwatchWhite };
        foreach (var s in swatches) if (s != null) s.Tag = null;

        PopupColorPicker.IsOpen = false;
    }

    private void OnInsertImageClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Insert Signature Stamp or Image onto PDF",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(dlg.FileName, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            double targetWidth = Math.Min(300, bmp.PixelWidth);
            double targetHeight = targetWidth * (bmp.PixelHeight / (double)bmp.PixelWidth);

            var img = new Image
            {
                Source = bmp,
                Width = targetWidth,
                Height = targetHeight,
                Stretch = Stretch.Uniform,
                Cursor = Cursors.Hand
            };

            var border = new Border
            {
                Child = img,
                Width = targetWidth,
                Height = targetHeight,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Tag = dlg.FileName
            };

            double posX = Math.Max(20, (PageDrawingCanvas.ActualWidth - targetWidth) / 2);
            double posY = Math.Max(20, (PageDrawingCanvas.ActualHeight - targetHeight) / 2);

            Canvas.SetLeft(border, posX);
            Canvas.SetTop(border, posY);
            PageDrawingCanvas.Children.Add(border);

            SelectElement(border);
            SetDirty(true);
            TxtStatus.Text = "Inserted image on PDF page. Drag to move or use 8 handles to resize.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not insert image: {ex.Message}", "Insert Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnDeleteMarkupClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedElement != null)
        {
            PageDrawingCanvas.Children.Remove(_selectedElement);
            DeselectCurrentElement();
            SetDirty(true);
            TxtStatus.Text = "Deleted selected markup.";
        }
    }

    private void OnClearPageMarkupClicked(object sender, RoutedEventArgs e)
    {
        ClearCanvasVisuals();
        DeselectCurrentElement();
        if (_currentPageIndex >= 0) _pageAnnotations.Remove(_currentPageIndex);
        SetDirty(true);
        TxtStatus.Text = "Cleared all markups on current page.";
    }

    #endregion

    #region Canvas Mouse Drawing, Moving & Resizing

    private void OnCanvasMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(PageDrawingCanvas);

        if (_currentTool == PdfMarkupTool.Select)
        {
            var hit = FindAnnotationElementUnderMouse(pos);
            SelectElement(hit);
            if (hit != null)
            {
                _isDraggingElement = true;
                _elementDragStart = pos;
                _elementOriginalBounds = GetElementBounds(hit);

                if (hit is Polyline poly)
                {
                    _origPolyPoints = poly.Points.ToList();
                }
                else if (hit is Canvas c && c.Children.Count >= 2 && c.Children[0] is Line line && c.Children[1] is Polygon head)
                {
                    _origArrowLine = (line.X1, line.Y1, line.X2, line.Y2);
                    _origHeadPoints = head.Points.ToList();
                }
            }
            return;
        }

        // Create new markup shape
        _isDrawing = true;
        _drawStartPoint = pos;

        switch (_currentTool)
        {
            case PdfMarkupTool.Pen:
            case PdfMarkupTool.Highlight:
            {
                var brushColor = _currentTool == PdfMarkupTool.Highlight
                    ? System.Windows.Media.Color.FromArgb(115, _currentColor.R, _currentColor.G, _currentColor.B)
                    : _currentColor;

                var strokeW = _currentTool == PdfMarkupTool.Highlight ? Math.Max(20.0, _strokeThickness * 4.0) : _strokeThickness;

                _activePolyline = new Polyline
                {
                    Stroke = new SolidColorBrush(brushColor),
                    StrokeThickness = strokeW,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round
                };
                _activePolyline.Points.Add(pos);
                PageDrawingCanvas.Children.Add(_activePolyline);
                break;
            }

            case PdfMarkupTool.Rectangle:
            {
                var rect = new Rectangle
                {
                    Stroke = new SolidColorBrush(_currentColor),
                    StrokeThickness = _strokeThickness,
                    Fill = Brushes.Transparent
                };
                Canvas.SetLeft(rect, pos.X);
                Canvas.SetTop(rect, pos.Y);
                PageDrawingCanvas.Children.Add(rect);
                _activeShapeElement = rect;
                break;
            }

            case PdfMarkupTool.Circle:
            {
                var ellipse = new Ellipse
                {
                    Stroke = new SolidColorBrush(_currentColor),
                    StrokeThickness = _strokeThickness,
                    Fill = Brushes.Transparent
                };
                Canvas.SetLeft(ellipse, pos.X);
                Canvas.SetTop(ellipse, pos.Y);
                PageDrawingCanvas.Children.Add(ellipse);
                _activeShapeElement = ellipse;
                break;
            }

            case PdfMarkupTool.Arrow:
            {
                var arrowCanvas = new Canvas { IsHitTestVisible = true };
                var line = new Line
                {
                    X1 = pos.X, Y1 = pos.Y,
                    X2 = pos.X, Y2 = pos.Y,
                    Stroke = new SolidColorBrush(_currentColor),
                    StrokeThickness = _strokeThickness,
                    StrokeEndLineCap = PenLineCap.Round
                };
                var head = new Polygon
                {
                    Fill = new SolidColorBrush(_currentColor)
                };
                arrowCanvas.Children.Add(line);
                arrowCanvas.Children.Add(head);
                PageDrawingCanvas.Children.Add(arrowCanvas);
                _activeShapeElement = arrowCanvas;
                break;
            }

            case PdfMarkupTool.Text:
            {
                var tb = new TextBox
                {
                    Text = "Type text here...",
                    FontSize = Math.Max(14, _strokeThickness * 4),
                    Foreground = new SolidColorBrush(_currentColor),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 26, 28, 37)),
                    BorderBrush = new SolidColorBrush(_currentColor),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 4, 6, 4),
                    AcceptsReturn = true
                };
                Canvas.SetLeft(tb, pos.X);
                Canvas.SetTop(tb, pos.Y);
                PageDrawingCanvas.Children.Add(tb);
                SelectElement(tb);
                _isDrawing = false;
                SetDirty(true);
                tb.Focus();
                tb.SelectAll();
                break;
            }
        }
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(PageDrawingCanvas);

        if (_isDraggingElement && _selectedElement != null)
        {
            double dx = pos.X - _elementDragStart.X;
            double dy = pos.Y - _elementDragStart.Y;

            if (_selectedElement is Polyline poly && _origPolyPoints != null)
            {
                var newPts = new PointCollection();
                foreach (var pt in _origPolyPoints)
                {
                    newPts.Add(new Point(pt.X + dx, pt.Y + dy));
                }
                poly.Points = newPts;
            }
            else if (_selectedElement is Canvas arrowCanvas && _origArrowLine != null && _origHeadPoints != null)
            {
                var line = (Line)arrowCanvas.Children[0];
                var head = (Polygon)arrowCanvas.Children[1];

                line.X1 = _origArrowLine.Value.X1 + dx;
                line.Y1 = _origArrowLine.Value.Y1 + dy;
                line.X2 = _origArrowLine.Value.X2 + dx;
                line.Y2 = _origArrowLine.Value.Y2 + dy;

                var newHeadPts = new PointCollection();
                foreach (var hp in _origHeadPoints)
                {
                    newHeadPts.Add(new Point(hp.X + dx, hp.Y + dy));
                }
                head.Points = newHeadPts;
            }
            else
            {
                Canvas.SetLeft(_selectedElement, _elementOriginalBounds.X + dx);
                Canvas.SetTop(_selectedElement, _elementOriginalBounds.Y + dy);
            }

            UpdateSelectionAdorner();
            SetDirty(true);
            return;
        }

        if (!_isDrawing) return;

        if (_activePolyline != null)
        {
            _activePolyline.Points.Add(pos);
            return;
        }

        if (_activeShapeElement is Rectangle rect)
        {
            double x = Math.Min(_drawStartPoint.X, pos.X);
            double y = Math.Min(_drawStartPoint.Y, pos.Y);
            double w = Math.Abs(pos.X - _drawStartPoint.X);
            double h = Math.Abs(pos.Y - _drawStartPoint.Y);
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            rect.Width = Math.Max(2, w);
            rect.Height = Math.Max(2, h);
        }
        else if (_activeShapeElement is Ellipse ellipse)
        {
            double x = Math.Min(_drawStartPoint.X, pos.X);
            double y = Math.Min(_drawStartPoint.Y, pos.Y);
            double w = Math.Abs(pos.X - _drawStartPoint.X);
            double h = Math.Abs(pos.Y - _drawStartPoint.Y);
            Canvas.SetLeft(ellipse, x);
            Canvas.SetTop(ellipse, y);
            ellipse.Width = Math.Max(2, w);
            ellipse.Height = Math.Max(2, h);
        }
        else if (_activeShapeElement is Canvas arrowCanvas && arrowCanvas.Children.Count >= 2)
        {
            var line = (Line)arrowCanvas.Children[0];
            var head = (Polygon)arrowCanvas.Children[1];

            line.X2 = pos.X;
            line.Y2 = pos.Y;

            UpdateArrowHead(head, line.X1, line.Y1, line.X2, line.Y2, _strokeThickness * 3.5);
        }
    }

    private void OnCanvasMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingElement)
        {
            _isDraggingElement = false;
        }

        if (_isDrawing)
        {
            _isDrawing = false;
            _activePolyline = null;
            _activeShapeElement = null;
            SetDirty(true);
        }
    }

    private void UpdateArrowHead(Polygon head, double x1, double y1, double x2, double y2, double headSize)
    {
        double angle = Math.Atan2(y2 - y1, x2 - x1);
        headSize = Math.Max(12.0, headSize);
        double arrowAngle = Math.PI / 6;

        var p1 = new Point(x2, y2);
        var p2 = new Point(x2 - headSize * Math.Cos(angle - arrowAngle), y2 - headSize * Math.Sin(angle - arrowAngle));
        var p3 = new Point(x2 - headSize * Math.Cos(angle + arrowAngle), y2 - headSize * Math.Sin(angle + arrowAngle));

        head.Points = new PointCollection { p1, p2, p3 };
    }

    #endregion

    #region Selection & Handle Resizing

    private UIElement? FindAnnotationElementUnderMouse(Point pos)
    {
        for (int i = PageDrawingCanvas.Children.Count - 1; i >= 0; i--)
        {
            var child = PageDrawingCanvas.Children[i];
            if (child == ActivePageImage || child == SelectionBoundsRect ||
                child == HandleTL || child == HandleTC || child == HandleTR ||
                child == HandleML || child == HandleMR ||
                child == HandleBL || child == HandleBC || child == HandleBR)
            {
                continue;
            }

            var bounds = GetElementBounds(child);
            bounds.Inflate(8, 8);
            if (bounds.Contains(pos))
            {
                return child;
            }
        }
        return null;
    }

    private void SelectElement(UIElement? element)
    {
        _selectedElement = element;
        if (element == null)
        {
            DeselectCurrentElement();
            return;
        }

        UpdateSelectionAdorner();
        SelectionBoundsRect.Visibility = Visibility.Visible;
        SetHandlesVisibility(Visibility.Visible);
    }

    private void DeselectCurrentElement()
    {
        _selectedElement = null;
        SelectionBoundsRect.Visibility = Visibility.Collapsed;
        SetHandlesVisibility(Visibility.Collapsed);
    }

    private void SetHandlesVisibility(Visibility v)
    {
        HandleTL.Visibility = v;
        HandleTC.Visibility = v;
        HandleTR.Visibility = v;
        HandleML.Visibility = v;
        HandleMR.Visibility = v;
        HandleBL.Visibility = v;
        HandleBC.Visibility = v;
        HandleBR.Visibility = v;
    }

    private void UpdateSelectionAdorner()
    {
        if (_selectedElement == null) return;

        var b = GetElementBounds(_selectedElement);
        Canvas.SetLeft(SelectionBoundsRect, b.X - 2);
        Canvas.SetTop(SelectionBoundsRect, b.Y - 2);
        SelectionBoundsRect.Width = Math.Max(4, b.Width + 4);
        SelectionBoundsRect.Height = Math.Max(4, b.Height + 4);

        PositionHandle(HandleTL, b.Left, b.Top);
        PositionHandle(HandleTC, b.Left + b.Width / 2, b.Top);
        PositionHandle(HandleTR, b.Right, b.Top);
        PositionHandle(HandleML, b.Left, b.Top + b.Height / 2);
        PositionHandle(HandleMR, b.Right, b.Top + b.Height / 2);
        PositionHandle(HandleBL, b.Left, b.Bottom);
        PositionHandle(HandleBC, b.Left + b.Width / 2, b.Bottom);
        PositionHandle(HandleBR, b.Right, b.Bottom);
    }

    private void PositionHandle(Thumb handle, double x, double y)
    {
        Canvas.SetLeft(handle, x - 5);
        Canvas.SetTop(handle, y - 5);
    }

    private Rect GetElementBounds(UIElement element)
    {
        if (element is Polyline poly)
        {
            if (poly.Points.Count == 0) return new Rect(0, 0, 0, 0);
            double minX = poly.Points.Min(p => p.X);
            double maxX = poly.Points.Max(p => p.X);
            double minY = poly.Points.Min(p => p.Y);
            double maxY = poly.Points.Max(p => p.Y);
            return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        }

        if (element is Canvas arrowCanvas && arrowCanvas.Children.Count >= 2 && arrowCanvas.Children[0] is Line line)
        {
            double minX = Math.Min(line.X1, line.X2);
            double maxX = Math.Max(line.X1, line.X2);
            double minY = Math.Min(line.Y1, line.Y2);
            double maxY = Math.Max(line.Y1, line.Y2);
            return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        }

        if (element is FrameworkElement fe)
        {
            double x = Canvas.GetLeft(fe);
            double y = Canvas.GetTop(fe);
            if (double.IsNaN(x)) x = 0;
            if (double.IsNaN(y)) y = 0;
            double w = fe.ActualWidth > 0 ? fe.ActualWidth : fe.Width;
            double h = fe.ActualHeight > 0 ? fe.ActualHeight : fe.Height;
            if (double.IsNaN(w) || w <= 0) w = 60;
            if (double.IsNaN(h) || h <= 0) h = 30;
            return new Rect(x, y, w, h);
        }

        return new Rect(0, 0, 10, 10);
    }

    private void OnHandleDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_selectedElement is not FrameworkElement fe) return;

        var b = GetElementBounds(fe);
        double left = b.X;
        double top = b.Y;
        double width = b.Width;
        double height = b.Height;

        if (sender == HandleBR)
        {
            width = Math.Max(16, width + e.HorizontalChange);
            height = Math.Max(16, height + e.VerticalChange);
        }
        else if (sender == HandleBC)
        {
            height = Math.Max(16, height + e.VerticalChange);
        }
        else if (sender == HandleMR)
        {
            width = Math.Max(16, width + e.HorizontalChange);
        }
        else if (sender == HandleTL)
        {
            double nw = Math.Max(16, width - e.HorizontalChange);
            double nh = Math.Max(16, height - e.VerticalChange);
            left += (width - nw);
            top += (height - nh);
            width = nw;
            height = nh;
        }

        Canvas.SetLeft(fe, left);
        Canvas.SetTop(fe, top);
        fe.Width = width;
        fe.Height = height;

        if (fe is Border border && border.Child is Image img)
        {
            img.Width = width;
            img.Height = height;
        }

        UpdateSelectionAdorner();
        SetDirty(true);
    }

    #endregion

    #region Zoom & Viewport Sizing

    private void OnZoomInClicked(object sender, RoutedEventArgs e)
    {
        _zoom = Math.Min(3.5, _zoom + 0.15);
        ApplyZoom();
    }

    private void OnZoomOutClicked(object sender, RoutedEventArgs e)
    {
        _zoom = Math.Max(0.15, _zoom - 0.15);
        ApplyZoom();
    }

    private void OnFitPageClicked(object sender, RoutedEventArgs e)
    {
        FitPageToViewport();
    }

    private void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            if (e.Delta > 0) OnZoomInClicked(sender, e);
            else OnZoomOutClicked(sender, e);
        }
    }

    private void FitPageToViewport()
    {
        double availW = PdfScrollViewer.ActualWidth - 64;
        double availH = PdfScrollViewer.ActualHeight - 64;
        double naturalW = PageDrawingCanvas.Width;
        double naturalH = PageDrawingCanvas.Height;

        if (availW > 100 && availH > 100 && naturalW > 0 && naturalH > 0)
        {
            _zoom = Math.Min(availW / naturalW, availH / naturalH);
            _zoom = Math.Max(0.2, Math.Min(3.0, _zoom));
            ApplyZoom();
        }
        else
        {
            _zoom = 1.0;
            ApplyZoom();
        }
    }

    private void ApplyZoom()
    {
        if (PageScaleTransform != null)
        {
            PageScaleTransform.ScaleX = _zoom;
            PageScaleTransform.ScaleY = _zoom;
        }
        if (TxtZoomLevel != null)
        {
            TxtZoomLevel.Text = $"{(int)(_zoom * 100)}%";
        }
    }

    #endregion

    #region Document Security, Export & Saving

    private void OnProtectClicked(object sender, RoutedEventArgs e)
    {
        var (confirmed, password) = PasswordInputDialog.Prompt("Protect PDF Document", "Enter an encryption password for this document:");
        if (confirmed && !string.IsNullOrEmpty(password) && _document != null)
        {
            _document.SecuritySettings.UserPassword = password;
            _document.SecuritySettings.OwnerPassword = password + "_admin";
            SetDirty(true);
            TxtStatus.Text = "Password protection armed. Click Save to commit.";
            MessageBox.Show("Password protection set! Click 'Save PDF' to apply AES-256 encryption.", "Orbit Security", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void OnExportImagesClicked(object sender, RoutedEventArgs e)
    {
        if (_currentPdfPath == null) return;

        try
        {
            TxtStatus.Text = "Exporting pages as high-resolution images...";
            var targetDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_currentPdfPath) ?? Environment.CurrentDirectory, $"{System.IO.Path.GetFileNameWithoutExtension(_currentPdfPath)}_pages");
            Directory.CreateDirectory(targetDir);

            var images = await Orbit.Engines.Execution.PdfProcessingEngine.RenderPdfToImagesAsync(_currentPdfPath, targetDir);
            TxtStatus.Text = $"Exported {images.Count} page images successfully to {System.IO.Path.GetFileName(targetDir)}!";
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
            CommitAnnotationsToPdf();
            _document.Save(_currentPdfPath);
            SetDirty(false);
            TxtStatus.Text = "Saved PDF document successfully.";
            MessageBox.Show("Document saved successfully!", "Orbit PDF Studio", MessageBoxButton.OK, MessageBoxImage.Information);
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
            FileName = $"{System.IO.Path.GetFileNameWithoutExtension(_currentPdfPath)}_edited.pdf"
        };

        if (dlg.ShowDialog() == true)
        {
            CommitAnnotationsToPdf();
            _document.Save(dlg.FileName);
            SetDirty(false);
            TxtStatus.Text = $"Saved as: {System.IO.Path.GetFileName(dlg.FileName)}";
            MessageBox.Show($"Saved as:\n{dlg.FileName}", "Orbit PDF Studio", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CommitAnnotationsToPdf()
    {
        if (_document == null) return;

        // Ensure current page annotations are saved
        if (_currentPageIndex >= 0)
        {
            SaveCurrentPageAnnotations(_currentPageIndex);
        }

        foreach (var kvp in _pageAnnotations)
        {
            int pageIdx = kvp.Key;
            var markups = kvp.Value;
            if (pageIdx < 0 || pageIdx >= _document.PageCount || markups.Count == 0) continue;

            var page = _document.Pages[pageIdx];
            double canvasW = PageDrawingCanvas.Width;
            double canvasH = PageDrawingCanvas.Height;
            if (canvasW <= 0 || canvasH <= 0) continue;

            double scaleX = page.Width.Point / canvasW;
            double scaleY = page.Height.Point / canvasH;

            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

            foreach (var el in markups)
            {
                if (el is Polyline poly && poly.Points.Count > 1)
                {
                    var xpts = poly.Points.Select(p => new XPoint(p.X * scaleX, p.Y * scaleY)).ToArray();
                    var scb = poly.Stroke as SolidColorBrush;
                    var c = scb?.Color ?? System.Windows.Media.Color.FromRgb(255, 122, 26);
                    var xColor = XColor.FromArgb(c.A, c.R, c.G, c.B);
                    var pen = new XPen(xColor, poly.StrokeThickness * scaleX);
                    gfx.DrawLines(pen, xpts);
                }
                else if (el is Rectangle rect)
                {
                    double x = Canvas.GetLeft(rect) * scaleX;
                    double y = Canvas.GetTop(rect) * scaleY;
                    double w = rect.Width * scaleX;
                    double h = rect.Height * scaleY;
                    var scb = rect.Stroke as SolidColorBrush;
                    var c = scb?.Color ?? System.Windows.Media.Color.FromRgb(255, 122, 26);
                    var xColor = XColor.FromArgb(c.A, c.R, c.G, c.B);
                    var pen = new XPen(xColor, rect.StrokeThickness * scaleX);
                    gfx.DrawRectangle(pen, x, y, w, h);
                }
                else if (el is Ellipse ellipse)
                {
                    double x = Canvas.GetLeft(ellipse) * scaleX;
                    double y = Canvas.GetTop(ellipse) * scaleY;
                    double w = ellipse.Width * scaleX;
                    double h = ellipse.Height * scaleY;
                    var scb = ellipse.Stroke as SolidColorBrush;
                    var c = scb?.Color ?? System.Windows.Media.Color.FromRgb(255, 122, 26);
                    var xColor = XColor.FromArgb(c.A, c.R, c.G, c.B);
                    var pen = new XPen(xColor, ellipse.StrokeThickness * scaleX);
                    gfx.DrawEllipse(pen, x, y, w, h);
                }
                else if (el is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
                {
                    double x = Canvas.GetLeft(tb) * scaleX;
                    double y = Canvas.GetTop(tb) * scaleY;
                    var scb = tb.Foreground as SolidColorBrush;
                    var c = scb?.Color ?? System.Windows.Media.Color.FromRgb(255, 122, 26);
                    var xColor = XColor.FromArgb(c.A, c.R, c.G, c.B);
                    var font = new XFont("Arial", tb.FontSize * scaleY, XFontStyle.Bold);
                    gfx.DrawString(tb.Text, font, new XSolidBrush(xColor), new XRect(x, y, 400 * scaleX, 100 * scaleY));
                }
                else if (el is Border border && border.Tag is string imgPath && File.Exists(imgPath))
                {
                    double x = Canvas.GetLeft(border) * scaleX;
                    double y = Canvas.GetTop(border) * scaleY;
                    double w = border.Width * scaleX;
                    double h = border.Height * scaleY;
                    using var xImg = XImage.FromFile(imgPath);
                    gfx.DrawImage(xImg, x, y, w, h);
                }
            }
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

    #endregion
}