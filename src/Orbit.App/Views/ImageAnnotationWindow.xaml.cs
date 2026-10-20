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
    public enum StudioMode
    {
        Markup,
        Adjust,
        Watermark,
        Transform
    }

    public enum AnnotationTool
    {
        Select,
        Pen,
        Arrow,
        Rectangle,
        Circle,
        Text,
        StepBadge,
        Blur,
        Highlight,
        Redact,
        Crop
    }

    private string? _currentImagePath;
    private BitmapSource? _originalBitmap;
    private BitmapSource? _baseTunedBitmap;

    private StudioMode _currentMode = StudioMode.Markup;
    private AnnotationTool _currentTool = AnnotationTool.Pen;
    private Color _currentColor = Color.FromRgb(0xEF, 0x44, 0x44); // Red default
    private double _strokeThickness = 4.0;
    private int _stepBadgeCounter = 1;

    private bool _isDrawing;
    private Point _startPoint;
    private FrameworkElement? _currentVisual;
    private FrameworkElement? _selectedElement;
    private bool _isDraggingElement;
    private Point _elementDragStart;
    private double _elementOriginalLeft;
    private double _elementOriginalTop;

    // Watermark element tracking
    private FrameworkElement? _activeWatermarkElement;
    private string? _watermarkLogoPath;

    private readonly Stack<UIElement> _undoStack = new();
    private readonly Stack<UIElement> _redoStack = new();

    public ImageAnnotationWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
        UpdateToolSelection(AnnotationTool.Pen);
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
            _originalBitmap = bitmap;
            _baseTunedBitmap = bitmap;
            ApplyBitmap(bitmap, IOPath.GetFileName(imagePath));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load image: {ex.Message}", "Orbit Image Studio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void LoadImageFromBitmap(BitmapSource bitmap, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        _currentImagePath = null;
        _originalBitmap = bitmap;
        _baseTunedBitmap = bitmap;
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
        if (DrawingCanvas == null) return;
        var elementsToRemove = new List<UIElement>();
        foreach (UIElement child in DrawingCanvas.Children)
        {
            if (child != BaseImageElement && child != _activeWatermarkElement)
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
        _stepBadgeCounter = 1;
        UpdateUndoRedoState();
    }

    #region Mode & Tool Selection

    public void SetMode(StudioMode mode, AnnotationTool? initialTool = null)
    {
        _currentMode = mode;

        if (BtnModeMarkup != null) BtnModeMarkup.Tag = (mode == StudioMode.Markup) ? "Active" : null;
        if (BtnModeAdjust != null) BtnModeAdjust.Tag = (mode == StudioMode.Adjust) ? "Active" : null;
        if (BtnModeWatermark != null) BtnModeWatermark.Tag = (mode == StudioMode.Watermark) ? "Active" : null;
        if (BtnModeTransform != null) BtnModeTransform.Tag = (mode == StudioMode.Transform) ? "Active" : null;

        if (PnlMarkupDetails != null) PnlMarkupDetails.Visibility = (mode == StudioMode.Markup) ? Visibility.Visible : Visibility.Collapsed;
        if (PnlAdjustments != null) PnlAdjustments.Visibility = (mode == StudioMode.Adjust) ? Visibility.Visible : Visibility.Collapsed;
        if (PnlWatermark != null) PnlWatermark.Visibility = (mode == StudioMode.Watermark) ? Visibility.Visible : Visibility.Collapsed;
        if (PnlTransform != null) PnlTransform.Visibility = (mode == StudioMode.Transform) ? Visibility.Visible : Visibility.Collapsed;

        if (initialTool.HasValue)
        {
            SetTool(initialTool.Value);
        }
        else if (mode == StudioMode.Transform)
        {
            SetTool(AnnotationTool.Crop);
        }
        else if (mode == StudioMode.Markup)
        {
            SetTool(AnnotationTool.Pen);
        }
        else
        {
            SetTool(AnnotationTool.Select);
        }
    }

    public void SetTool(AnnotationTool tool)
    {
        _currentTool = tool;
        UpdateToolSelection(tool);
    }

    private void OnModeTabClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        if (btn == BtnModeMarkup) SetMode(StudioMode.Markup);
        else if (btn == BtnModeAdjust) SetMode(StudioMode.Adjust);
        else if (btn == BtnModeWatermark) SetMode(StudioMode.Watermark);
        else if (btn == BtnModeTransform) SetMode(StudioMode.Transform);
    }

    private void OnGoToWatermarkClicked(object sender, RoutedEventArgs e) => SetMode(StudioMode.Watermark);
    private void OnGoToAdjustClicked(object sender, RoutedEventArgs e) => SetMode(StudioMode.Adjust);

    private void OnToolSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        if (btn == BtnToolSelect) SetTool(AnnotationTool.Select);
        else if (btn == BtnToolPen) SetTool(AnnotationTool.Pen);
        else if (btn == BtnToolArrow) SetTool(AnnotationTool.Arrow);
        else if (btn == BtnToolRectangle) SetTool(AnnotationTool.Rectangle);
        else if (btn == BtnToolCircle) SetTool(AnnotationTool.Circle);
        else if (btn == BtnToolText) SetTool(AnnotationTool.Text);
        else if (btn == BtnToolStepBadge) SetTool(AnnotationTool.StepBadge);
        else if (btn == BtnToolBlur) SetTool(AnnotationTool.Blur);
        else if (btn == BtnToolHighlight) SetTool(AnnotationTool.Highlight);
        else if (btn == BtnToolRedact) SetTool(AnnotationTool.Redact);
    }

    private void UpdateToolSelection(AnnotationTool tool)
    {
        Button[] tools = [BtnToolSelect, BtnToolPen, BtnToolArrow, BtnToolRectangle, BtnToolCircle, BtnToolText, BtnToolStepBadge, BtnToolBlur, BtnToolHighlight, BtnToolRedact];
        foreach (var t in tools)
        {
            if (t != null) t.Tag = null;
        }

        var activeBtn = tool switch
        {
            AnnotationTool.Select => BtnToolSelect,
            AnnotationTool.Pen => BtnToolPen,
            AnnotationTool.Arrow => BtnToolArrow,
            AnnotationTool.Rectangle => BtnToolRectangle,
            AnnotationTool.Circle => BtnToolCircle,
            AnnotationTool.Text => BtnToolText,
            AnnotationTool.StepBadge => BtnToolStepBadge,
            AnnotationTool.Blur => BtnToolBlur,
            AnnotationTool.Highlight => BtnToolHighlight,
            AnnotationTool.Redact => BtnToolRedact,
            _ => null
        };

        if (activeBtn != null) activeBtn.Tag = "Active";

        if (DrawingCanvas != null)
        {
            DrawingCanvas.Cursor = tool switch
            {
                AnnotationTool.Select => Cursors.Arrow,
                AnnotationTool.Pen => Cursors.Pen,
                AnnotationTool.Text => Cursors.IBeam,
                AnnotationTool.Crop => Cursors.Cross,
                _ => Cursors.Cross
            };
        }

        string hint = tool switch
        {
            AnnotationTool.Select => "Tool: Select (Click to move/resize annotations)",
            AnnotationTool.Pen => "Tool: Pen (Freehand drawing)",
            AnnotationTool.Arrow => "Tool: Arrow (Click & drag pointing arrow)",
            AnnotationTool.Rectangle => "Tool: Rectangle (Drag box shape)",
            AnnotationTool.Circle => "Tool: Circle (Drag ellipse shape)",
            AnnotationTool.Text => "Tool: Text (Click to place text label)",
            AnnotationTool.StepBadge => $"Tool: Step Marker (Click to place badge #{_stepBadgeCounter})",
            AnnotationTool.Blur => "Tool: Blur (Drag over sensitive details)",
            AnnotationTool.Highlight => "Tool: Highlighter (Drag translucent highlight)",
            AnnotationTool.Redact => "Tool: Redact (Drag solid blackout privacy bar)",
            AnnotationTool.Crop => "Tool: Crop (Drag bounding box to crop)",
            _ => "Tool: Active"
        };

        if (TxtToolHint != null) TxtToolHint.Text = hint;
        if (TxtActiveToolName != null) TxtActiveToolName.Text = hint.Replace("Tool: ", "");
    }

    private void OnColorSwatchClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        Button[] swatches = [SwatchRed, SwatchOrange, SwatchYellow, SwatchGreen, SwatchCyan, SwatchWhite, SwatchBlack];
        foreach (var s in swatches)
        {
            if (s != null) s.Tag = null;
        }

        btn.Tag = "Selected";

        if (btn.Background is SolidColorBrush scb)
        {
            _currentColor = scb.Color;
        }
    }

    private void OnStrokeWidthCycleClicked(object sender, RoutedEventArgs e)
    {
        _strokeThickness = _strokeThickness switch
        {
            2.0 => 4.0,
            4.0 => 8.0,
            8.0 => 12.0,
            12.0 => 16.0,
            _ => 2.0
        };

        if (BtnStrokeWidth != null)
        {
            BtnStrokeWidth.Content = $"{(int)_strokeThickness}px";
        }
    }

    #endregion

    #region Canvas Mouse Drawing & Interactions

    private void OnCanvasMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(DrawingCanvas);

        if (_currentTool == AnnotationTool.Select)
        {
            var hit = FindAnnotationElementUnderMouse(pos);
            SelectElement(hit);
            if (hit != null)
            {
                _isDraggingElement = true;
                _elementDragStart = pos;
                _elementOriginalLeft = Canvas.GetLeft(hit);
                _elementOriginalTop = Canvas.GetTop(hit);
                if (double.IsNaN(_elementOriginalLeft)) _elementOriginalLeft = 0;
                if (double.IsNaN(_elementOriginalTop)) _elementOriginalTop = 0;
                DrawingCanvas.CaptureMouse();
            }
            return;
        }

        if (_currentTool == AnnotationTool.Text)
        {
            PromptAddTextAnnotation(pos);
            return;
        }

        if (_currentTool == AnnotationTool.StepBadge)
        {
            PlaceStepBadge(pos);
            return;
        }

        // Shape / Pen / Crop drawing start
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
            if (_currentTool == AnnotationTool.Pen && _currentVisual is Polyline poly)
            {
                poly.Points.Add(cur);
            }
            else
            {
                UpdateDrawingVisual(_currentTool, _currentVisual, _startPoint, cur);
            }
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

        if (_currentTool == AnnotationTool.Pen && _currentVisual is Polyline poly)
        {
            if (poly.Points.Count > 1)
            {
                _undoStack.Push(poly);
                _redoStack.Clear();
                UpdateUndoRedoState();
                SelectElement(poly);
            }
            else
            {
                DrawingCanvas.Children.Remove(poly);
            }
        }
        else if (dist > 4.0)
        {
            if (_currentTool == AnnotationTool.Crop)
            {
                ApplyCropSelection(_currentVisual);
                DrawingCanvas.Children.Remove(_currentVisual);
            }
            else
            {
                _undoStack.Push(_currentVisual);
                _redoStack.Clear();
                UpdateUndoRedoState();
                SelectElement(_currentVisual);
            }
        }
        else
        {
            DrawingCanvas.Children.Remove(_currentVisual);
        }

        _currentVisual = null;
    }

    private FrameworkElement? CreateDrawingVisual(AnnotationTool tool, Point start)
    {
        var brush = new SolidColorBrush(_currentColor);

        switch (tool)
        {
            case AnnotationTool.Pen:
                var polyline = new Polyline
                {
                    Stroke = brush,
                    StrokeThickness = _strokeThickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round
                };
                polyline.Points.Add(start);
                return polyline;

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
                if (visual is Rectangle br && br.Fill is VisualBrush vb)
                {
                    vb.Viewbox = new Rect(x, y, w, h);
                    vb.ViewboxUnits = BrushMappingMode.Absolute;
                }
                break;

            case AnnotationTool.Arrow:
                if (visual is Canvas c && c.Children.Count >= 2 &&
                    c.Children[0] is Line line &&
                    c.Children[1] is Polygon head)
                {
                    line.X1 = start.X;
                    line.Y1 = start.Y;
                    line.X2 = current.X;
                    line.Y2 = current.Y;

                    double angle = Math.Atan2(current.Y - start.Y, current.X - start.X);
                    double headLen = Math.Max(12, _strokeThickness * 3.5);
                    double headAngle = Math.PI / 6;

                    Point p1 = current;
                    Point p2 = new(
                        current.X - headLen * Math.Cos(angle - headAngle),
                        current.Y - headLen * Math.Sin(angle - headAngle));
                    Point p3 = new(
                        current.X - headLen * Math.Cos(angle + headAngle),
                        current.Y - headLen * Math.Sin(angle + headAngle));

                    head.Points = [p1, p2, p3];
                }
                break;
        }
    }

    private void PlaceStepBadge(Point pos)
    {
        var badge = new Grid
        {
            Width = 28,
            Height = 28,
            Cursor = Cursors.Hand
        };

        var circle = new Ellipse
        {
            Fill = new SolidColorBrush(_currentColor),
            Stroke = Brushes.White,
            StrokeThickness = 2
        };

        var text = new TextBlock
        {
            Text = _stepBadgeCounter.ToString(),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        badge.Children.Add(circle);
        badge.Children.Add(text);

        Canvas.SetLeft(badge, Math.Max(0, pos.X - 14));
        Canvas.SetTop(badge, Math.Max(0, pos.Y - 14));

        DrawingCanvas.Children.Add(badge);
        _undoStack.Push(badge);
        _redoStack.Clear();
        UpdateUndoRedoState();
        SelectElement(badge);

        _stepBadgeCounter++;
        UpdateToolSelection(AnnotationTool.StepBadge);
    }

    private void PromptAddTextAnnotation(Point pos)
    {
        var inputWindow = new Window
        {
            Title = "Add Text Annotation",
            Width = 360,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x19, 0x20)),
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize
        };

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var textBox = new TextBox
        {
            Text = "Annotation Label",
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromRgb(0x23, 0x25, 0x30)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x35, 0x37, 0x45)),
            Padding = new Thickness(8, 6, 8, 6),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(textBox, 0);
        grid.Children.Add(textBox);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(btnPanel, 1);

        var btnOk = new Button
        {
            Content = "Add Text",
            Width = 90,
            Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x1A)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var btnCancel = new Button
        {
            Content = "Cancel",
            Width = 80,
            Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x2F, 0x3E)),
            Foreground = Brushes.White
        };

        btnOk.Click += (s, e) => { inputWindow.DialogResult = true; inputWindow.Close(); };
        btnCancel.Click += (s, e) => { inputWindow.DialogResult = false; inputWindow.Close(); };

        btnPanel.Children.Add(btnOk);
        btnPanel.Children.Add(btnCancel);
        grid.Children.Add(btnPanel);
        inputWindow.Content = grid;

        textBox.SelectAll();
        textBox.Focus();

        if (inputWindow.ShowDialog() == true && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            var textElement = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x18, 0x19, 0x20)),
                BorderBrush = new SolidColorBrush(_currentColor),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Child = new TextBlock
                {
                    Text = textBox.Text,
                    Foreground = new SolidColorBrush(_currentColor),
                    FontSize = Math.Max(12, _strokeThickness * 3.5),
                    FontWeight = FontWeights.SemiBold
                }
            };

            Canvas.SetLeft(textElement, pos.X);
            Canvas.SetTop(textElement, pos.Y);
            DrawingCanvas.Children.Add(textElement);
            _undoStack.Push(textElement);
            _redoStack.Clear();
            UpdateUndoRedoState();
            SelectElement(textElement);
        }
    }

    private void SelectElement(FrameworkElement? element)
    {
        if (_selectedElement != null)
        {
            _selectedElement.Opacity = 1.0;
        }

        _selectedElement = element;
        if (_selectedElement != null)
        {
            _selectedElement.Opacity = 0.85;
            if (BtnDeleteSelected != null) BtnDeleteSelected.IsEnabled = true;
        }
        else
        {
            if (BtnDeleteSelected != null) BtnDeleteSelected.IsEnabled = false;
        }
    }

    private FrameworkElement? FindAnnotationElementUnderMouse(Point pos)
    {
        for (int i = DrawingCanvas.Children.Count - 1; i >= 0; i--)
        {
            var child = DrawingCanvas.Children[i];
            if (child == BaseImageElement || child == _activeWatermarkElement) continue;

            if (child is FrameworkElement fe)
            {
                double left = Canvas.GetLeft(fe);
                double top = Canvas.GetTop(fe);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;

                var bounds = new Rect(left, top, Math.Max(10, fe.ActualWidth), Math.Max(10, fe.ActualHeight));
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
            _originalBitmap = cropped;
            _baseTunedBitmap = cropped;
            ApplyBitmap(cropped, IOPath.GetFileName(_currentImagePath ?? "Cropped"));
            if (TxtStatusMessage != null) TxtStatusMessage.Text = $"Cropped canvas to {cw} × {ch}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not crop image: {ex.Message}", "Crop Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    #endregion

    #region Live Adjustments & Color Tuning

    private void OnAdjustmentSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_originalBitmap == null || BaseImageElement == null) return;

        int brightness = (int)(SliderBrightness?.Value ?? 0);
        int contrast = (int)(SliderContrast?.Value ?? 0);
        int saturation = (int)(SliderSaturation?.Value ?? 0);
        double blur = SliderBlur?.Value ?? 0;

        if (TxtValBrightness != null) TxtValBrightness.Text = brightness > 0 ? $"+{brightness}" : brightness.ToString();
        if (TxtValContrast != null) TxtValContrast.Text = contrast > 0 ? $"+{contrast}" : contrast.ToString();
        if (TxtValSaturation != null) TxtValSaturation.Text = saturation > 0 ? $"+{saturation}" : saturation.ToString();
        if (TxtValBlur != null) TxtValBlur.Text = $"{(int)blur}px";

        // Generate adjusted bitmap buffer
        var adjusted = GenerateAdjustedBitmap(_originalBitmap, brightness, contrast, saturation);
        BaseImageElement.Source = adjusted;
        _baseTunedBitmap = adjusted;

        // Apply real-time blur effect
        BaseImageElement.Effect = blur > 0.5 ? new BlurEffect { Radius = blur } : null;
    }

    private static BitmapSource GenerateAdjustedBitmap(BitmapSource source, int brightness, int contrast, int saturation)
    {
        if (brightness == 0 && contrast == 0 && saturation == 0) return source;

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        converted.CopyPixels(pixels, stride, 0);

        double cFactor = (259.0 * (contrast + 255.0)) / (255.0 * (259.0 - contrast));
        double sFactor = 1.0 + (saturation / 100.0);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];

            // 1. Contrast & Brightness
            double dr = cFactor * (r - 128) + 128 + brightness;
            double dg = cFactor * (g - 128) + 128 + brightness;
            double db = cFactor * (b - 128) + 128 + brightness;

            // 2. Saturation
            if (saturation != 0)
            {
                double gray = 0.299 * dr + 0.587 * dg + 0.114 * db;
                dr = gray + (dr - gray) * sFactor;
                dg = gray + (dg - gray) * sFactor;
                db = gray + (db - gray) * sFactor;
            }

            pixels[i] = (byte)Math.Clamp(db, 0, 255);
            pixels[i + 1] = (byte)Math.Clamp(dg, 0, 255);
            pixels[i + 2] = (byte)Math.Clamp(dr, 0, 255);
        }

        var result = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        result.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        result.Freeze();
        return result;
    }

    private void OnPresetVividClicked(object sender, RoutedEventArgs e)
    {
        if (SliderBrightness != null) SliderBrightness.Value = 5;
        if (SliderContrast != null) SliderContrast.Value = 25;
        if (SliderSaturation != null) SliderSaturation.Value = 35;
        if (SliderBlur != null) SliderBlur.Value = 0;
    }

    private void OnPresetMonoClicked(object sender, RoutedEventArgs e)
    {
        if (SliderBrightness != null) SliderBrightness.Value = 0;
        if (SliderContrast != null) SliderContrast.Value = 15;
        if (SliderSaturation != null) SliderSaturation.Value = -100;
        if (SliderBlur != null) SliderBlur.Value = 0;
    }

    private void OnPresetSepiaClicked(object sender, RoutedEventArgs e)
    {
        if (SliderBrightness != null) SliderBrightness.Value = 10;
        if (SliderContrast != null) SliderContrast.Value = -10;
        if (SliderSaturation != null) SliderSaturation.Value = -50;
        if (SliderBlur != null) SliderBlur.Value = 0;
    }

    private void OnPresetHighContrastClicked(object sender, RoutedEventArgs e)
    {
        if (SliderBrightness != null) SliderBrightness.Value = 0;
        if (SliderContrast != null) SliderContrast.Value = 50;
        if (SliderSaturation != null) SliderSaturation.Value = 10;
        if (SliderBlur != null) SliderBlur.Value = 0;
    }

    private void OnResetAdjustmentsClicked(object sender, RoutedEventArgs e)
    {
        if (SliderBrightness != null) SliderBrightness.Value = 0;
        if (SliderContrast != null) SliderContrast.Value = 0;
        if (SliderSaturation != null) SliderSaturation.Value = 0;
        if (SliderBlur != null) SliderBlur.Value = 0;
        if (_originalBitmap != null && BaseImageElement != null)
        {
            BaseImageElement.Source = _originalBitmap;
            BaseImageElement.Effect = null;
            _baseTunedBitmap = _originalBitmap;
        }
    }

    #endregion

    #region Watermark Studio

    private void OnWatermarkTypeChanged(object sender, RoutedEventArgs e)
    {
        bool isText = RbWatermarkText?.IsChecked == true;
        if (SecWatermarkText != null) SecWatermarkText.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        if (SecWatermarkLogo != null) SecWatermarkLogo.Visibility = !isText ? Visibility.Visible : Visibility.Collapsed;
        UpdateWatermarkPreview();
    }

    private void OnWatermarkInputChanged(object sender, RoutedEventArgs e)
    {
        if (TxtValWatermarkSize != null && SliderWatermarkSize != null)
            TxtValWatermarkSize.Text = $"{(int)SliderWatermarkSize.Value}px";
        if (TxtValWatermarkOpacity != null && SliderWatermarkOpacity != null)
            TxtValWatermarkOpacity.Text = $"{(int)SliderWatermarkOpacity.Value}%";
        if (TxtValWatermarkScale != null && SliderWatermarkScale != null)
            TxtValWatermarkScale.Text = $"{(int)SliderWatermarkScale.Value}%";

        UpdateWatermarkPreview();
    }

    private void OnWatermarkPositionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateWatermarkPreview();
    }

    private void OnBrowseWatermarkLogoClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Logo Images (*.png;*.jpg;*.webp;*.svg)|*.png;*.jpg;*.webp;*.svg|All Files (*.*)|*.*",
            Title = "Choose Watermark Logo"
        };

        if (dlg.ShowDialog() == true)
        {
            _watermarkLogoPath = dlg.FileName;
            if (TxtWatermarkLogoPath != null) TxtWatermarkLogoPath.Text = IOPath.GetFileName(dlg.FileName);
            UpdateWatermarkPreview();
        }
    }

    private void UpdateWatermarkPreview()
    {
        if (DrawingCanvas == null || DrawingCanvas.Width <= 0 || DrawingCanvas.Height <= 0) return;

        // Remove previous watermark visual if present
        if (_activeWatermarkElement != null && DrawingCanvas.Children.Contains(_activeWatermarkElement))
        {
            DrawingCanvas.Children.Remove(_activeWatermarkElement);
            _activeWatermarkElement = null;
        }

        bool isText = RbWatermarkText?.IsChecked == true;
        double opacity = (SliderWatermarkOpacity?.Value ?? 60.0) / 100.0;
        string position = (CmbWatermarkPosition?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Bottom-Right";

        FrameworkElement visual;

        if (isText)
        {
            string watermarkText = TxtWatermarkInput?.Text ?? "ORBIT";
            if (string.IsNullOrWhiteSpace(watermarkText)) return;
            double fontSize = SliderWatermarkSize?.Value ?? 36.0;

            if (position == "Diagonal Tiled")
            {
                var tileCanvas = new Canvas
                {
                    Width = DrawingCanvas.Width,
                    Height = DrawingCanvas.Height,
                    IsHitTestVisible = false,
                    Opacity = opacity
                };

                for (double y = 40; y < DrawingCanvas.Height; y += 120)
                {
                    for (double x = 40; x < DrawingCanvas.Width; x += 220)
                    {
                        var tb = new TextBlock
                        {
                            Text = watermarkText,
                            FontSize = fontSize,
                            Foreground = new SolidColorBrush(_currentColor),
                            FontWeight = FontWeights.Bold,
                            RenderTransform = new RotateTransform(-25)
                        };
                        Canvas.SetLeft(tb, x);
                        Canvas.SetTop(tb, y);
                        tileCanvas.Children.Add(tb);
                    }
                }
                visual = tileCanvas;
            }
            else
            {
                var tb = new TextBlock
                {
                    Text = watermarkText,
                    FontSize = fontSize,
                    Foreground = new SolidColorBrush(_currentColor),
                    FontWeight = FontWeights.Bold,
                    Opacity = opacity,
                    IsHitTestVisible = false
                };
                visual = tb;
            }
        }
        else
        {
            if (string.IsNullOrEmpty(_watermarkLogoPath) || !File.Exists(_watermarkLogoPath)) return;
            try
            {
                var bmp = new BitmapImage(new Uri(_watermarkLogoPath));
                double scale = (SliderWatermarkScale?.Value ?? 40.0) / 100.0;
                var img = new Image
                {
                    Source = bmp,
                    Width = Math.Max(20, bmp.PixelWidth * scale),
                    Opacity = opacity,
                    IsHitTestVisible = false
                };
                visual = img;
            }
            catch
            {
                return;
            }
        }

        // Measure and arrange visual
        visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double vw = visual.DesiredSize.Width > 0 ? visual.DesiredSize.Width : 200;
        double vh = visual.DesiredSize.Height > 0 ? visual.DesiredSize.Height : 50;

        if (position != "Diagonal Tiled")
        {
            double posX = position switch
            {
                "Bottom-Left" or "Top-Left" => 24,
                "Center" => (DrawingCanvas.Width - vw) / 2,
                _ => Math.Max(10, DrawingCanvas.Width - vw - 24)
            };

            double posY = position switch
            {
                "Top-Left" or "Top-Right" => 24,
                "Center" => (DrawingCanvas.Height - vh) / 2,
                _ => Math.Max(10, DrawingCanvas.Height - vh - 24)
            };

            Canvas.SetLeft(visual, posX);
            Canvas.SetTop(visual, posY);
        }

        _activeWatermarkElement = visual;
        DrawingCanvas.Children.Add(visual);
    }

    private void OnApplyWatermarkClicked(object sender, RoutedEventArgs e)
    {
        UpdateWatermarkPreview();
        if (TxtStatusMessage != null) TxtStatusMessage.Text = "Applied watermark overlay to canvas";
    }

    private void OnRemoveWatermarkClicked(object sender, RoutedEventArgs e)
    {
        if (_activeWatermarkElement != null && DrawingCanvas.Children.Contains(_activeWatermarkElement))
        {
            DrawingCanvas.Children.Remove(_activeWatermarkElement);
            _activeWatermarkElement = null;
            if (TxtStatusMessage != null) TxtStatusMessage.Text = "Watermark removed";
        }
    }

    #endregion

    #region Transform & Rotation

    private void OnStartCropClicked(object sender, RoutedEventArgs e)
    {
        SetTool(AnnotationTool.Crop);
    }

    private void OnAspectPresetClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        string tag = btn.Tag?.ToString() ?? "Free";

        if (DrawingCanvas.Width <= 0 || DrawingCanvas.Height <= 0) return;

        double targetW = DrawingCanvas.Width * 0.75;
        double targetH = DrawingCanvas.Height * 0.75;

        switch (tag)
        {
            case "1:1":
                double side = Math.Min(targetW, targetH);
                targetW = side;
                targetH = side;
                break;
            case "16:9":
                targetH = targetW * (9.0 / 16.0);
                break;
            case "4:3":
                targetH = targetW * (3.0 / 4.0);
                break;
        }

        var marquee = new Rectangle
        {
            Width = targetW,
            Height = targetH,
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x1A)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0x7A, 0x1A))
        };

        Canvas.SetLeft(marquee, (DrawingCanvas.Width - targetW) / 2);
        Canvas.SetTop(marquee, (DrawingCanvas.Height - targetH) / 2);

        DrawingCanvas.Children.Add(marquee);
        ApplyCropSelection(marquee);
        DrawingCanvas.Children.Remove(marquee);
    }

    private void OnRotateCWClicked(object sender, RoutedEventArgs e)
    {
        if (_originalBitmap == null) return;
        var transformed = new TransformedBitmap(_originalBitmap, new RotateTransform(90));
        transformed.Freeze();
        _originalBitmap = transformed;
        _baseTunedBitmap = transformed;
        ApplyBitmap(transformed, IOPath.GetFileName(_currentImagePath ?? "Rotated"));
    }

    private void OnRotateCCWClicked(object sender, RoutedEventArgs e)
    {
        if (_originalBitmap == null) return;
        var transformed = new TransformedBitmap(_originalBitmap, new RotateTransform(270));
        transformed.Freeze();
        _originalBitmap = transformed;
        _baseTunedBitmap = transformed;
        ApplyBitmap(transformed, IOPath.GetFileName(_currentImagePath ?? "Rotated"));
    }

    private void OnFlipHorizontalClicked(object sender, RoutedEventArgs e)
    {
        if (_originalBitmap == null) return;
        var transformed = new TransformedBitmap(_originalBitmap, new ScaleTransform(-1, 1, _originalBitmap.PixelWidth / 2.0, 0));
        transformed.Freeze();
        _originalBitmap = transformed;
        _baseTunedBitmap = transformed;
        ApplyBitmap(transformed, IOPath.GetFileName(_currentImagePath ?? "Flipped_H"));
    }

    private void OnFlipVerticalClicked(object sender, RoutedEventArgs e)
    {
        if (_originalBitmap == null) return;
        var transformed = new TransformedBitmap(_originalBitmap, new ScaleTransform(1, -1, 0, _originalBitmap.PixelHeight / 2.0));
        transformed.Freeze();
        _originalBitmap = transformed;
        _baseTunedBitmap = transformed;
        ApplyBitmap(transformed, IOPath.GetFileName(_currentImagePath ?? "Flipped_V"));
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
        }
        else
        {
            DrawingCanvas.Children.Remove(item);
        }
        _redoStack.Push(item);
        UpdateUndoRedoState();
        SelectElement(null);
    }

    private void OnRedoClicked(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;

        var item = _redoStack.Pop();
        if (item is DeletedAnnotationAction del)
        {
            del.Redo();
        }
        else
        {
            DrawingCanvas.Children.Add(item);
        }
        _undoStack.Push(item);
        UpdateUndoRedoState();
        SelectElement(item as FrameworkElement);
    }

    private void OnDeleteSelectedClicked(object sender, RoutedEventArgs e) => DeleteSelectedElement();

    private void OnClearAllClicked(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Clear all markings and annotations?", "Clear Canvas", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            ClearCanvasAnnotations();
        }
    }

    private void UpdateUndoRedoState()
    {
        if (BtnUndo != null) BtnUndo.IsEnabled = _undoStack.Count > 0;
        if (BtnRedo != null) BtnRedo.IsEnabled = _redoStack.Count > 0;
    }

    #endregion

    #region Zoom & Viewport

    private void OnZoomInClicked(object sender, RoutedEventArgs e) => AdjustZoom(0.25);
    private void OnZoomOutClicked(object sender, RoutedEventArgs e) => AdjustZoom(-0.25);
    private void OnZoomResetClicked(object sender, RoutedEventArgs e) => ApplyZoomToFit();

    private void AdjustZoom(double delta)
    {
        if (CanvasScaleTransform == null) return;
        double current = CanvasScaleTransform.ScaleX;
        double newZoom = Math.Clamp(current + delta, 0.25, 4.0);
        SetZoom(newZoom);
    }

    private void SetZoom(double zoom)
    {
        if (CanvasScaleTransform == null) return;
        CanvasScaleTransform.ScaleX = zoom;
        CanvasScaleTransform.ScaleY = zoom;
        if (TxtStatusMessage != null) TxtStatusMessage.Text = $"Zoom: {(int)(zoom * 100)}%";
    }

    private void ApplyZoomToFit()
    {
        if (BaseImageElement?.Source is not BitmapSource src || CanvasScaleTransform == null) return;

        double availW = ImageScrollViewer?.ActualWidth > 0 ? ImageScrollViewer.ActualWidth - 64 : 800;
        double availH = ImageScrollViewer?.ActualHeight > 0 ? ImageScrollViewer.ActualHeight - 64 : 600;

        double scaleX = availW / src.PixelWidth;
        double scaleY = availH / src.PixelHeight;
        double best = Math.Min(1.0, Math.Min(scaleX, scaleY));

        SetZoom(Math.Max(0.1, best));
    }

    private void OnZoomSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbZoom?.SelectedItem is not ComboBoxItem item || CanvasScaleTransform == null) return;
        string content = item.Content.ToString() ?? "";

        if (content.Equals("Fit", StringComparison.OrdinalIgnoreCase))
        {
            ApplyZoomToFit();
        }
        else if (content.EndsWith('%'))
        {
            if (double.TryParse(content.TrimEnd('%'), out double pct))
            {
                SetZoom(pct / 100.0);
            }
        }
    }

    private void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            AdjustZoom(e.Delta > 0 ? 0.15 : -0.15);
            e.Handled = true;
        }
    }

    #endregion

    #region Save, Export & Clipboard

    private void OnOpenImageClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tiff)|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tiff|All Files (*.*)|*.*",
            Title = "Select Image to Open"
        };

        if (dlg.ShowDialog() == true)
        {
            LoadImage(dlg.FileName);
        }
    }

    private void OnCopyClipboardClicked(object sender, RoutedEventArgs e)
    {
        CopyImageToClipboard();
    }

    public void CopyImageToClipboard()
    {
        try
        {
            var rtb = RenderCompositedBitmap();
            if (rtb != null)
            {
                Clipboard.SetImage(rtb);
                if (TxtStatusMessage != null) TxtStatusMessage.Text = "Copied composited image to clipboard!";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not copy image: {ex.Message}", "Orbit", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        if (TxtStatusMessage != null) TxtStatusMessage.Text = $"Saved in place to {_currentImagePath}";
    }

    private void OnSaveAsClicked(object sender, RoutedEventArgs e)
    {
        if (BaseImageElement?.Source == null)
        {
            MessageBox.Show("Please open an image first.", "Orbit Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var defaultName = string.IsNullOrEmpty(_currentImagePath) 
            ? "orbit_export.png" 
            : IOPath.GetFileNameWithoutExtension(_currentImagePath) + "_edited.png";

        var dlg = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp",
            FileName = defaultName,
            DefaultExt = ".png"
        };

        if (dlg.ShowDialog() == true)
        {
            SaveCanvasToFile(dlg.FileName);
            if (TxtStatusMessage != null) TxtStatusMessage.Text = $"Saved exported image to {dlg.FileName}";
        }
    }

    public void SaveCanvasToFile(string destinationPath)
    {
        var rtb = RenderCompositedBitmap();
        if (rtb == null) return;

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

    private RenderTargetBitmap? RenderCompositedBitmap()
    {
        if (DrawingCanvas == null || DrawingCanvas.Width <= 0 || DrawingCanvas.Height <= 0) return null;

        SelectElement(null);

        double originalScaleX = CanvasScaleTransform?.ScaleX ?? 1.0;
        double originalScaleY = CanvasScaleTransform?.ScaleY ?? 1.0;

        if (CanvasScaleTransform != null)
        {
            CanvasScaleTransform.ScaleX = 1.0;
            CanvasScaleTransform.ScaleY = 1.0;
        }

        int width = (int)DrawingCanvas.Width;
        int height = (int)DrawingCanvas.Height;

        DrawingCanvas.Measure(new Size(width, height));
        DrawingCanvas.Arrange(new Rect(0, 0, width, height));
        DrawingCanvas.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(DrawingCanvas);

        if (CanvasScaleTransform != null)
        {
            CanvasScaleTransform.ScaleX = originalScaleX;
            CanvasScaleTransform.ScaleY = originalScaleY;
        }

        return rtb;
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
        else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CopyImageToClipboard();
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back && _selectedElement != null)
        {
            DeleteSelectedElement();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None)
        {
            switch (e.Key)
            {
                case Key.P: SetTool(AnnotationTool.Pen); e.Handled = true; break;
                case Key.A: SetTool(AnnotationTool.Arrow); e.Handled = true; break;
                case Key.R: SetTool(AnnotationTool.Rectangle); e.Handled = true; break;
                case Key.O: SetTool(AnnotationTool.Circle); e.Handled = true; break;
                case Key.T: SetTool(AnnotationTool.Text); e.Handled = true; break;
                case Key.S: SetTool(AnnotationTool.StepBadge); e.Handled = true; break;
                case Key.B: SetTool(AnnotationTool.Blur); e.Handled = true; break;
                case Key.C: SetTool(AnnotationTool.Crop); e.Handled = true; break;
                case Key.V: SetTool(AnnotationTool.Select); e.Handled = true; break;
            }
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
