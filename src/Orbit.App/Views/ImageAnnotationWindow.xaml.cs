namespace Orbit.App.Views;

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

using IOPath = System.IO.Path;

public partial class ImageAnnotationWindow : Window
{
    public enum AnnotationTool
    {
        Select,
        Crop,
        Arrow,
        Rectangle,
        Circle,
        Text,
        Blur,
        Highlight,
        Redact
    }

    private string? _currentImagePath;
    private AnnotationTool _currentTool = AnnotationTool.Select;
    private Color _currentColor = Color.FromRgb(0xEF, 0x44, 0x44); // Red default
    private double _strokeThickness = 4.0;
    private bool _isDrawing;
    private Point _startPoint;
    private FrameworkElement? _currentVisual;
    private FrameworkElement? _selectedElement;
    private bool _isDraggingElement;
    private Point _elementDragStart;
    private double _elementOriginalLeft;
    private double _elementOriginalTop;

    private readonly Stack<UIElement> _undoStack = new();
    private readonly Stack<UIElement> _redoStack = new();

    public ImageAnnotationWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
        UpdateToolSelection(AnnotationTool.Select);
        if (CmbZoom != null)
        {
            CmbZoom.SelectionChanged += OnZoomSelectionChanged;
        }
    }

    public void LoadImage(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(IOPath.GetFullPath(imagePath));
            bitmap.EndInit();
            bitmap.Freeze();

            _currentImagePath = imagePath;
            ApplyBitmap(bitmap, IOPath.GetFileName(imagePath));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load image: {ex.Message}", "Orbit Image Annotator", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void LoadImageFromBitmap(BitmapSource bitmap, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        _currentImagePath = null;
        ApplyBitmap(bitmap, name ?? "Clipboard_Image");
    }

    private void ApplyBitmap(BitmapSource bitmap, string displayName)
    {
        if (bitmap == null) return;
        if (BaseImageElement == null || DrawingCanvas == null) return;

        BaseImageElement.Source = bitmap;
        DrawingCanvas.Width = bitmap.PixelWidth;
        DrawingCanvas.Height = bitmap.PixelHeight;

        // Clear existing marks
        ClearCanvasAnnotations();

        if (TxtImageMeta != null) TxtImageMeta.Text = $"{displayName} • {bitmap.PixelWidth} × {bitmap.PixelHeight} px";
        if (EmptyStatePrompt != null) EmptyStatePrompt.Visibility = Visibility.Collapsed;
        if (TxtStatusMessage != null) TxtStatusMessage.Text = $"Loaded {displayName}";

        // Auto zoom to fit if large
        Dispatcher.BeginInvoke(() =>
        {
            if (CanvasScaleTransform != null && (bitmap.PixelWidth > ActualWidth * 0.8 || bitmap.PixelHeight > ActualHeight * 0.8))
            {
                ApplyZoomToFit();
            }
            else
            {
                SetZoom(1.0);
            }
        });
    }

    private void ClearCanvasAnnotations()
    {
        var elementsToRemove = new List<UIElement>();
        foreach (UIElement child in DrawingCanvas.Children)
        {
            if (child != BaseImageElement)
            {
                elementsToRemove.Add(child);
            }
        }

        foreach (var el in elementsToRemove)
        {
            DrawingCanvas.Children.Remove(el);
        }

        _undoStack.Clear();
        _redoStack.Clear();
        _selectedElement = null;
        UpdateUndoRedoState();
    }

    #region Tool Selection & Toolbar Handlers

    private void OnToolSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        var tool = btn.Name switch
        {
            "BtnToolSelect" => AnnotationTool.Select,
            "BtnToolCrop" => AnnotationTool.Crop,
            "BtnToolArrow" => AnnotationTool.Arrow,
            "BtnToolRectangle" => AnnotationTool.Rectangle,
            "BtnToolCircle" => AnnotationTool.Circle,
            "BtnToolText" => AnnotationTool.Text,
            "BtnToolBlur" => AnnotationTool.Blur,
            "BtnToolHighlight" => AnnotationTool.Highlight,
            "BtnToolRedact" => AnnotationTool.Redact,
            _ => AnnotationTool.Select
        };

        UpdateToolSelection(tool);
    }

    public void SetTool(AnnotationTool tool)
    {
        UpdateToolSelection(tool);
    }

    private void UpdateToolSelection(AnnotationTool tool)
    {
        _currentTool = tool;

        // Reset tags on all tool buttons
        if (BtnToolSelect != null) BtnToolSelect.Tag = null;
        if (BtnToolCrop != null) BtnToolCrop.Tag = null;
        if (BtnToolArrow != null) BtnToolArrow.Tag = null;
        if (BtnToolRectangle != null) BtnToolRectangle.Tag = null;
        if (BtnToolCircle != null) BtnToolCircle.Tag = null;
        if (BtnToolText != null) BtnToolText.Tag = null;
        if (BtnToolBlur != null) BtnToolBlur.Tag = null;
        if (BtnToolHighlight != null) BtnToolHighlight.Tag = null;
        if (BtnToolRedact != null) BtnToolRedact.Tag = null;

        // Set active tag
        var activeBtn = tool switch
        {
            AnnotationTool.Select => BtnToolSelect,
            AnnotationTool.Crop => BtnToolCrop,
            AnnotationTool.Arrow => BtnToolArrow,
            AnnotationTool.Rectangle => BtnToolRectangle,
            AnnotationTool.Circle => BtnToolCircle,
            AnnotationTool.Text => BtnToolText,
            AnnotationTool.Blur => BtnToolBlur,
            AnnotationTool.Highlight => BtnToolHighlight,
            AnnotationTool.Redact => BtnToolRedact,
            _ => BtnToolSelect
        };
        if (activeBtn != null) activeBtn.Tag = "Active";

        if (TxtToolHint != null)
        {
            TxtToolHint.Text = tool switch
            {
                AnnotationTool.Select => "Tool: Select (Click to select/drag annotations, Del to remove)",
                AnnotationTool.Crop => "Tool: Crop (Drag marquee to crop image canvas)",
                AnnotationTool.Arrow => "Tool: Arrow (Click & drag to draw directional arrowhead)",
                AnnotationTool.Rectangle => "Tool: Rectangle (Click & drag to frame target area)",
                AnnotationTool.Circle => "Tool: Circle (Click & drag to encircle key details)",
                AnnotationTool.Text => "Tool: Text Callout (Click anywhere to place editable text)",
                AnnotationTool.Blur => "Tool: Blur Sensitive Area (Drag box over credentials/faces)",
                AnnotationTool.Highlight => "Tool: Highlight (Draw translucent highlighter strip)",
                AnnotationTool.Redact => "Tool: Redact (Drag solid black bar over private info)",
                _ => "Tool: Ready"
            };
        }
    }

    private void OnColorSwatchClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        // Deselect all
        SwatchRed.Tag = null;
        SwatchOrange.Tag = null;
        SwatchYellow.Tag = null;
        SwatchGreen.Tag = null;
        SwatchCyan.Tag = null;
        SwatchWhite.Tag = null;
        SwatchBlack.Tag = null;

        btn.Tag = "Selected";
        if (btn.Background is SolidColorBrush brush)
        {
            _currentColor = brush.Color;
        }
    }

    private void OnStrokeWidthCycleClicked(object sender, RoutedEventArgs e)
    {
        _strokeThickness = _strokeThickness switch
        {
            2.0 => 4.0,
            4.0 => 8.0,
            8.0 => 12.0,
            _ => 2.0
        };

        BtnStrokeWidth.Content = $"{_strokeThickness}px";
    }

    private void OnDeleteSelectedClicked(object sender, RoutedEventArgs e)
    {
        DeleteSelectedElement();
    }

    private void OnClearAllClicked(object sender, RoutedEventArgs e)
    {
        if (DrawingCanvas.Children.Count <= 1) return;

        if (MessageBox.Show("Are you sure you want to clear all annotations?", "Orbit Annotator", 
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            ClearCanvasAnnotations();
        }
    }

    #endregion

    #region Canvas Drawing & Mouse Interaction

    private void OnCanvasMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (BaseImageElement.Source == null) return;

        var pos = e.GetPosition(DrawingCanvas);

        if (_currentTool == AnnotationTool.Select)
        {
            // Check if user clicked an existing annotation
            var clickedVisual = FindAnnotationElementUnderMouse(e.GetPosition(DrawingCanvas));
            SelectElement(clickedVisual);

            if (_selectedElement != null)
            {
                _isDraggingElement = true;
                _elementDragStart = pos;
                _elementOriginalLeft = Canvas.GetLeft(_selectedElement);
                _elementOriginalTop = Canvas.GetTop(_selectedElement);
                if (double.IsNaN(_elementOriginalLeft)) _elementOriginalLeft = 0;
                if (double.IsNaN(_elementOriginalTop)) _elementOriginalTop = 0;
                DrawingCanvas.CaptureMouse();
            }
            return;
        }

        if (_currentTool == AnnotationTool.Text)
        {
            CreateTextAnnotation(pos);
            UpdateToolSelection(AnnotationTool.Select);
            return;
        }

        // Shape drawing start
        _isDrawing = true;
        _startPoint = pos;
        _currentVisual = CreateDrawingVisual(_currentTool, _startPoint);

        if (_currentVisual != null)
        {
            DrawingCanvas.Children.Add(_currentVisual);
            DrawingCanvas.CaptureMouse();
        }
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        var cur = e.GetPosition(DrawingCanvas);

        if (_isDraggingElement && _selectedElement != null)
        {
            double dx = cur.X - _elementDragStart.X;
            double dy = cur.Y - _elementDragStart.Y;
            Canvas.SetLeft(_selectedElement, _elementOriginalLeft + dx);
            Canvas.SetTop(_selectedElement, _elementOriginalTop + dy);
            return;
        }

        if (_isDrawing && _currentVisual != null)
        {
            UpdateDrawingVisual(_currentTool, _currentVisual, _startPoint, cur);
        }
    }

    private void OnCanvasMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingElement)
        {
            _isDraggingElement = false;
            DrawingCanvas.ReleaseMouseCapture();
            return;
        }

        if (!_isDrawing || _currentVisual == null) return;

        _isDrawing = false;
        DrawingCanvas.ReleaseMouseCapture();

        var cur = e.GetPosition(DrawingCanvas);
        double dist = (cur - _startPoint).Length;

        if (dist > 4.0)
        {
            if (_currentTool == AnnotationTool.Crop)
            {
                ApplyCropSelection(_currentVisual);
                DrawingCanvas.Children.Remove(_currentVisual);
            }
            else
            {
                // Finalize annotation element
                _undoStack.Push(_currentVisual);
                _redoStack.Clear();
                UpdateUndoRedoState();
                SelectElement(_currentVisual);
            }
        }
        else
        {
            // Point too small, remove preview
            DrawingCanvas.Children.Remove(_currentVisual);
        }

        _currentVisual = null;
    }

    private FrameworkElement? CreateDrawingVisual(AnnotationTool tool, Point start)
    {
        var brush = new SolidColorBrush(_currentColor);

        switch (tool)
        {
            case AnnotationTool.Rectangle:
                var rect = new Rectangle
                {
                    Stroke = brush,
                    StrokeThickness = _strokeThickness,
                    RadiusX = 4,
                    RadiusY = 4,
                    Fill = new SolidColorBrush(Color.FromArgb(0x18, _currentColor.R, _currentColor.G, _currentColor.B))
                };
                Canvas.SetLeft(rect, start.X);
                Canvas.SetTop(rect, start.Y);
                return rect;

            case AnnotationTool.Circle:
                var ellipse = new Ellipse
                {
                    Stroke = brush,
                    StrokeThickness = _strokeThickness,
                    Fill = new SolidColorBrush(Color.FromArgb(0x18, _currentColor.R, _currentColor.G, _currentColor.B))
                };
                Canvas.SetLeft(ellipse, start.X);
                Canvas.SetTop(ellipse, start.Y);
                return ellipse;

            case AnnotationTool.Arrow:
                var arrowCanvas = new Canvas { Width = 1, Height = 1 };
                var line = new Line
                {
                    Stroke = brush,
                    StrokeThickness = _strokeThickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    X1 = start.X,
                    Y1 = start.Y,
                    X2 = start.X,
                    Y2 = start.Y
                };
                var head = new Polygon
                {
                    Fill = brush,
                    Points = new PointCollection()
                };
                arrowCanvas.Children.Add(line);
                arrowCanvas.Children.Add(head);
                return arrowCanvas;

            case AnnotationTool.Blur:
                // Visual blur region
                var blurRect = new Rectangle
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(0x88, 0x38, 0xBD, 0xF8)),
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 3, 2 },
                    Fill = new VisualBrush(BaseImageElement)
                    {
                        Stretch = Stretch.None,
                        AlignmentX = AlignmentX.Left,
                        AlignmentY = AlignmentY.Top
                    },
                    Effect = new BlurEffect { Radius = 18 }
                };
                Canvas.SetLeft(blurRect, start.X);
                Canvas.SetTop(blurRect, start.Y);
                return blurRect;

            case AnnotationTool.Redact:
                var redactRect = new Rectangle
                {
                    Fill = Brushes.Black,
                    Stroke = Brushes.Black,
                    StrokeThickness = 0
                };
                Canvas.SetLeft(redactRect, start.X);
                Canvas.SetTop(redactRect, start.Y);
                return redactRect;

            case AnnotationTool.Highlight:
                var highlightRect = new Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(0x55, _currentColor.R, _currentColor.G, _currentColor.B)),
                    Stroke = null,
                    RadiusX = 2,
                    RadiusY = 2
                };
                Canvas.SetLeft(highlightRect, start.X);
                Canvas.SetTop(highlightRect, start.Y);
                return highlightRect;

            case AnnotationTool.Crop:
                var cropMarquee = new Rectangle
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x1A)),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    Fill = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0x7A, 0x1A))
                };
                Canvas.SetLeft(cropMarquee, start.X);
                Canvas.SetTop(cropMarquee, start.Y);
                return cropMarquee;
        }

        return null;
    }

    private void UpdateDrawingVisual(AnnotationTool tool, FrameworkElement visual, Point start, Point current)
    {
        double x = Math.Min(start.X, current.X);
        double y = Math.Min(start.Y, current.Y);
        double w = Math.Max(1, Math.Abs(current.X - start.X));
        double h = Math.Max(1, Math.Abs(current.Y - start.Y));

        switch (tool)
        {
            case AnnotationTool.Rectangle:
            case AnnotationTool.Circle:
            case AnnotationTool.Redact:
            case AnnotationTool.Highlight:
            case AnnotationTool.Crop:
                visual.Width = w;
                visual.Height = h;
                Canvas.SetLeft(visual, x);
                Canvas.SetTop(visual, y);
                break;

            case AnnotationTool.Blur:
                visual.Width = w;
                visual.Height = h;
                Canvas.SetLeft(visual, x);
                Canvas.SetTop(visual, y);
                if (visual is Rectangle r && r.Fill is VisualBrush vb && BaseImageElement.Source is BitmapSource src)
                {
                    vb.Viewbox = new Rect(x, y, w, h);
                    vb.ViewboxUnits = BrushMappingMode.Absolute;
                }
                break;

            case AnnotationTool.Arrow:
                if (visual is Canvas arrowCanvas && arrowCanvas.Children.Count >= 2)
                {
                    var line = (Line)arrowCanvas.Children[0];
                    var head = (Polygon)arrowCanvas.Children[1];

                    line.X1 = start.X;
                    line.Y1 = start.Y;
                    line.X2 = current.X;
                    line.Y2 = current.Y;

                    // Compute arrowhead polygon
                    double angle = Math.Atan2(current.Y - start.Y, current.X - start.X);
                    double arrowSize = Math.Max(14, _strokeThickness * 3.5);
                    double arrowAngle = 28.0 * Math.PI / 180.0;

                    Point p1 = current;
                    Point p2 = new(
                        current.X - arrowSize * Math.Cos(angle - arrowAngle),
                        current.Y - arrowSize * Math.Sin(angle - arrowAngle));
                    Point p3 = new(
                        current.X - arrowSize * Math.Cos(angle + arrowAngle),
                        current.Y - arrowSize * Math.Sin(angle + arrowAngle));

                    head.Points.Clear();
                    head.Points.Add(p1);
                    head.Points.Add(p2);
                    head.Points.Add(p3);
                }
                break;
        }
    }

    private void CreateTextAnnotation(Point pos)
    {
        var brush = new SolidColorBrush(_currentColor);
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x18, 0x19, 0x20)),
            BorderBrush = brush,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4)
        };

        var textBox = new TextBox
        {
            Text = "Annotation Text",
            Foreground = brush,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = brush,
            MinWidth = 120
        };

        border.Child = textBox;
        Canvas.SetLeft(border, pos.X);
        Canvas.SetTop(border, pos.Y);

        DrawingCanvas.Children.Add(border);
        _undoStack.Push(border);
        _redoStack.Clear();
        UpdateUndoRedoState();
        SelectElement(border);

        Dispatcher.BeginInvoke(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        });
    }

    private void SelectElement(FrameworkElement? element)
    {
        // Unselect prior
        if (_selectedElement != null && _selectedElement != element)
        {
            if (_selectedElement is Shape s) s.Opacity = 1.0;
            if (_selectedElement is Border b) b.Opacity = 1.0;
        }

        _selectedElement = element;
        if (_selectedElement != null)
        {
            // Give subtle selection highlight
            _selectedElement.Opacity = 0.85;
            BtnDeleteSelected.IsEnabled = true;
        }
        else
        {
            BtnDeleteSelected.IsEnabled = false;
        }
    }

    private FrameworkElement? FindAnnotationElementUnderMouse(Point pos)
    {
        // Search backwards (top-most first)
        for (int i = DrawingCanvas.Children.Count - 1; i >= 0; i--)
        {
            var child = DrawingCanvas.Children[i];
            if (child == BaseImageElement) continue;

            if (child is FrameworkElement fe)
            {
                double left = Canvas.GetLeft(fe);
                double top = Canvas.GetTop(fe);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;

                var bounds = new Rect(left, top, fe.ActualWidth, fe.ActualHeight);
                if (bounds.Contains(pos))
                {
                    return fe;
                }
            }
        }
        return null;
    }

    private void DeleteSelectedElement()
    {
        if (_selectedElement == null) return;

        var el = _selectedElement;
        DrawingCanvas.Children.Remove(el);
        _undoStack.Push(new DeletedAnnotationAction(el, DrawingCanvas));
        SelectElement(null);
        UpdateUndoRedoState();
    }

    private void ApplyCropSelection(FrameworkElement cropMarquee)
    {
        double x = Canvas.GetLeft(cropMarquee);
        double y = Canvas.GetTop(cropMarquee);
        double w = cropMarquee.Width;
        double h = cropMarquee.Height;

        if (BaseImageElement.Source is not BitmapSource src) return;

        int cx = Math.Max(0, (int)x);
        int cy = Math.Max(0, (int)y);
        int cw = Math.Min(src.PixelWidth - cx, (int)w);
        int ch = Math.Min(src.PixelHeight - cy, (int)h);

        if (cw < 10 || ch < 10) return;

        try
        {
            var cropped = new CroppedBitmap(src, new Int32Rect(cx, cy, cw, ch));
            ApplyBitmap(cropped, IOPath.GetFileName(_currentImagePath ?? "Cropped"));
            TxtStatusMessage.Text = $"Cropped canvas to {cw} × {ch}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not crop image: {ex.Message}", "Crop Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    #endregion

    #region Undo / Redo

    private void OnUndoClicked(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0) return;

        var item = _undoStack.Pop();
        if (item is DeletedAnnotationAction del)
        {
            del.Undo();
            _redoStack.Push(del);
        }
        else
        {
            DrawingCanvas.Children.Remove(item);
            _redoStack.Push(item);
        }

        SelectElement(null);
        UpdateUndoRedoState();
    }

    private void OnRedoClicked(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;

        var item = _redoStack.Pop();
        if (item is DeletedAnnotationAction del)
        {
            del.Redo();
            _undoStack.Push(del);
        }
        else
        {
            DrawingCanvas.Children.Add(item);
            _undoStack.Push(item);
        }

        UpdateUndoRedoState();
    }

    private void UpdateUndoRedoState()
    {
        BtnUndo.IsEnabled = _undoStack.Count > 0;
        BtnRedo.IsEnabled = _redoStack.Count > 0;
    }

    #endregion

    #region Zoom Controls

    private void OnZoomInClicked(object sender, RoutedEventArgs e)
    {
        SetZoom(CanvasScaleTransform.ScaleX * 1.25);
    }

    private void OnZoomOutClicked(object sender, RoutedEventArgs e)
    {
        SetZoom(CanvasScaleTransform.ScaleX * 0.8);
    }

    private void OnZoomResetClicked(object sender, RoutedEventArgs e)
    {
        SetZoom(1.0);
    }

    private void OnZoomSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbZoom == null || CanvasScaleTransform == null) return;
        if (CmbZoom.SelectedItem is not ComboBoxItem item) return;
        var tag = item.Content?.ToString();

        if (tag == "Fit")
        {
            ApplyZoomToFit();
        }
        else if (tag != null && tag.EndsWith("%") && double.TryParse(tag.TrimEnd('%'), out var percent))
        {
            SetZoom(percent / 100.0);
        }
    }

    private void SetZoom(double zoom)
    {
        if (CanvasScaleTransform == null) return;
        zoom = Math.Clamp(zoom, 0.1, 5.0);
        CanvasScaleTransform.ScaleX = zoom;
        CanvasScaleTransform.ScaleY = zoom;
    }

    private void ApplyZoomToFit()
    {
        if (BaseImageElement?.Source == null || ImageScrollViewer == null || DrawingCanvas == null) return;
        double viewW = ImageScrollViewer.ActualWidth - 64;
        double viewH = ImageScrollViewer.ActualHeight - 64;

        if (viewW <= 0 || viewH <= 0 || DrawingCanvas.Width <= 0 || DrawingCanvas.Height <= 0) return;

        double scaleX = viewW / DrawingCanvas.Width;
        double scaleY = viewH / DrawingCanvas.Height;
        double fitScale = Math.Min(scaleX, scaleY);
        SetZoom(fitScale);
    }

    private void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Delta > 0)
            {
                SetZoom(CanvasScaleTransform.ScaleX * 1.15);
            }
            else
            {
                SetZoom(CanvasScaleTransform.ScaleX * 0.85);
            }
            e.Handled = true;
        }
    }

    #endregion

    #region Save & Export (RenderTargetBitmap)

    private void OnOpenImageClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tiff)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tiff|All Files (*.*)|*.*",
            Title = "Select Image to Annotate"
        };

        if (dlg.ShowDialog() == true)
        {
            LoadImage(dlg.FileName);
        }
    }

    private void OnQuickSaveClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentImagePath))
        {
            OnSaveAsClicked(sender, e);
            return;
        }

        SaveCanvasToFile(_currentImagePath);
        TxtStatusMessage.Text = $"Saved in place to {_currentImagePath}";
    }

    private void OnSaveAsClicked(object sender, RoutedEventArgs e)
    {
        if (BaseImageElement.Source == null)
        {
            MessageBox.Show("Please open an image first.", "Orbit Annotator", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var defaultName = string.IsNullOrEmpty(_currentImagePath) 
            ? "orbit_annotation.png" 
            : IOPath.GetFileNameWithoutExtension(_currentImagePath) + "_annotated.png";

        var dlg = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp",
            FileName = defaultName,
            DefaultExt = ".png"
        };

        if (dlg.ShowDialog() == true)
        {
            SaveCanvasToFile(dlg.FileName);
            TxtStatusMessage.Text = $"Saved exported image to {dlg.FileName}";
        }
    }

    public void SaveCanvasToFile(string destinationPath)
    {
        SelectElement(null);

        // Temporarily reset zoom to 1.0 for pixel-perfect render
        double originalScaleX = CanvasScaleTransform.ScaleX;
        double originalScaleY = CanvasScaleTransform.ScaleY;
        CanvasScaleTransform.ScaleX = 1.0;
        CanvasScaleTransform.ScaleY = 1.0;

        int width = (int)DrawingCanvas.Width;
        int height = (int)DrawingCanvas.Height;

        DrawingCanvas.Measure(new Size(width, height));
        DrawingCanvas.Arrange(new Rect(0, 0, width, height));
        DrawingCanvas.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(DrawingCanvas);

        // Restore active display zoom
        CanvasScaleTransform.ScaleX = originalScaleX;
        CanvasScaleTransform.ScaleY = originalScaleY;

        string ext = IOPath.GetExtension(destinationPath).ToLowerInvariant();
        BitmapEncoder encoder = ext switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
            ".bmp" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };

        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using (var stream = File.Create(destinationPath))
        {
            encoder.Save(stream);
        }
    }

    #endregion

    #region Drag & Drop and Keys

    private void OnCanvasDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnCanvasDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files != null && files.Length > 0)
        {
            LoadImage(files[0]);
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OnUndoClicked(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OnRedoClicked(sender, e);
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back && _selectedElement != null)
        {
            DeleteSelectedElement();
            e.Handled = true;
        }
    }

    #endregion

    private sealed class DeletedAnnotationAction : UIElement
    {
        private readonly UIElement _element;
        private readonly Canvas _canvas;

        public DeletedAnnotationAction(UIElement element, Canvas canvas)
        {
            _element = element;
            _canvas = canvas;
        }

        public void Undo()
        {
            if (!_canvas.Children.Contains(_element))
            {
                _canvas.Children.Add(_element);
            }
        }

        public void Redo()
        {
            _canvas.Children.Remove(_element);
        }
    }
}
