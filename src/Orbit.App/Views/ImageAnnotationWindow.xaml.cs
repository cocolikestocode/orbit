namespace Orbit.App.Views;

using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    private double _strokeThickness = 6.0;
    private double _strokeOpacity = 1.0;
    private int _stepBadgeCounter = 1;
    private bool _hasUnsavedChanges = false;

    private bool _isDrawing;
    private Point _startPoint;
    private FrameworkElement? _currentVisual;
    private FrameworkElement? _selectedElement;
    private bool _isDraggingElement;
    private Point _elementDragStart;
    private Rect _elementOriginalBounds;

    private enum ResizeHandle
    {
        None,
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left
    }

    private ResizeHandle _activeResizeHandle = ResizeHandle.None;
    private Point _resizeStartMouse;
    private Rect _resizeStartBounds;
    private List<Point>? _origPolyPoints;
    private (double X1, double Y1, double X2, double Y2) _origArrowLine;
    private List<Point>? _origHeadPoints;

    // Watermark state (Disabled by default)
    private bool _isWatermarkActive = false;
    private FrameworkElement? _activeWatermarkElement;
    private string? _watermarkLogoPath;

    private readonly Stack<UIElement> _undoStack = new();
    private readonly Stack<UIElement> _redoStack = new();

    public ImageAnnotationWindow()
    {
        InitializeComponent();
        KeyDown += OnWindowKeyDown;
        Closing += OnWindowClosing;
        UpdateToolSelection(AnnotationTool.Pen);
        UpdateStrokePreview();

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
            SetDirty(false);
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
        SetDirty(false);
    }

    private void ApplyBitmap(BitmapSource bitmap, string displayName, bool clearAnnotations = true)
    {
        if (bitmap == null) return;
        if (BaseImageElement == null || DrawingCanvas == null) return;

        BaseImageElement.Source = bitmap;
        DrawingCanvas.Width = bitmap.PixelWidth;
        DrawingCanvas.Height = bitmap.PixelHeight;
        if (SelectionOverlayCanvas != null)
        {
            SelectionOverlayCanvas.Width = bitmap.PixelWidth;
            SelectionOverlayCanvas.Height = bitmap.PixelHeight;
        }

        if (clearAnnotations)
        {
            ClearCanvasAnnotations();
        }

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
        if (TxtNextBadgeNumber != null) TxtNextBadgeNumber.Text = "Badge #1";
        UpdateUndoRedoState();
    }

    #region Dirty / Saved State Tracking

    private void SetDirty(bool dirty)
    {
        _hasUnsavedChanges = dirty;

        if (BadgeSavedState != null) BadgeSavedState.Visibility = dirty ? Visibility.Collapsed : Visibility.Visible;
        if (BadgeUnsavedState != null) BadgeUnsavedState.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;

        if (BtnSaveQuick != null)
        {
            BtnSaveQuick.Content = dirty ? "💾 Save" : "Saved";
            if (dirty)
            {
                BtnSaveQuick.Foreground = Brushes.White;
                BtnSaveQuick.Background = (Brush)FindResource("OrbitAccentBrush");
            }
            else
            {
                BtnSaveQuick.Foreground = (Brush)FindResource("OrbitTextSecondaryBrush");
                BtnSaveQuick.Background = (Brush)FindResource("OrbitSurfaceHoverBrush");
            }
        }

        string fileName = IOPath.GetFileName(_currentImagePath ?? "New Image");
        Title = $"Orbit — Image Studio • {(dirty ? "Unsaved Changes" : "Saved")} - {fileName}";
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_hasUnsavedChanges) return;

        var result = MessageBox.Show(
            "You have unsaved changes in this image. Do you want to save before closing?",
            "Orbit Image Studio",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            OnQuickSaveClicked(this, new RoutedEventArgs());
        }
        else if (result == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
        }
    }

    #endregion

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

        if (ColLeftToolbar != null)
        {
            ColLeftToolbar.Width = (mode == StudioMode.Markup) ? new GridLength(60) : new GridLength(0);
        }

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

        if (tool != AnnotationTool.Select)
        {
            SelectElement(null);
        }

        if (tool == AnnotationTool.Crop)
        {
            ShowCropOverlay();
        }
        else
        {
            HideCropOverlay();
        }
    }

    private void OnModeTabClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        if (btn == BtnModeMarkup) SetMode(StudioMode.Markup);
        else if (btn == BtnModeAdjust) SetMode(StudioMode.Adjust);
        else if (btn == BtnModeWatermark) SetMode(StudioMode.Watermark);
        else if (btn == BtnModeTransform) SetMode(StudioMode.Transform);
    }

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
        else if (btn == BtnToolHighlight) SetTool(AnnotationTool.Highlight);
        else if (btn == BtnToolBlur) SetTool(AnnotationTool.Blur);
        else if (btn == BtnToolRedact) SetTool(AnnotationTool.Redact);
    }

    private void UpdateToolSelection(AnnotationTool tool)
    {
        Button[] tools = [BtnToolSelect, BtnToolPen, BtnToolArrow, BtnToolRectangle, BtnToolCircle, BtnToolText, BtnToolStepBadge, BtnToolHighlight, BtnToolBlur, BtnToolRedact];
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
            AnnotationTool.Highlight => BtnToolHighlight,
            AnnotationTool.Blur => BtnToolBlur,
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

        // Update Right Inspector Context
        if (TxtInspectorHeader != null)
        {
            TxtInspectorHeader.Text = tool switch
            {
                AnnotationTool.Pen => "PEN & BRUSH PROPERTIES",
                AnnotationTool.Arrow => "ARROW PROPERTIES",
                AnnotationTool.Rectangle => "RECTANGLE PROPERTIES",
                AnnotationTool.Circle => "CIRCLE PROPERTIES",
                AnnotationTool.Text => "TEXT CALLOUT PROPERTIES",
                AnnotationTool.StepBadge => "NUMBERED STEP BADGES",
                AnnotationTool.Highlight => "HIGHLIGHTER PROPERTIES",
                AnnotationTool.Blur => "BLUR REDACTION PROPERTIES",
                AnnotationTool.Redact => "BLACKOUT PRIVACY PROPERTIES",
                _ => "ELEMENT PROPERTIES"
            };
        }

        if (SecStepBadgeOptions != null)
        {
            SecStepBadgeOptions.Visibility = (tool == AnnotationTool.StepBadge) ? Visibility.Visible : Visibility.Collapsed;
        }

        if (SecThicknessSlider != null)
        {
            SecThicknessSlider.Visibility = (tool == AnnotationTool.StepBadge) ? Visibility.Collapsed : Visibility.Visible;
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
            AnnotationTool.Highlight => "Tool: Highlighter (Drag translucent highlight)",
            AnnotationTool.Blur => "Tool: Blur (Drag over sensitive details)",
            AnnotationTool.Redact => "Tool: Redact (Drag solid blackout privacy bar)",
            AnnotationTool.Crop => "Tool: Crop (Drag bounding box to crop)",
            _ => "Tool: Active"
        };

        if (TxtToolHint != null) TxtToolHint.Text = hint;
    }

    private void OnThicknessSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _strokeThickness = SliderThickness?.Value ?? 6.0;
        if (TxtValThickness != null) TxtValThickness.Text = $"{(int)_strokeThickness} px";
        UpdateStrokePreview();
    }

    private void OnOpacitySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _strokeOpacity = (SliderOpacity?.Value ?? 100.0) / 100.0;
        if (TxtValOpacity != null) TxtValOpacity.Text = $"{(int)(_strokeOpacity * 100)}%";
        UpdateStrokePreview();
    }

    private void UpdateStrokePreview()
    {
        if (EllipseStrokePreview == null) return;
        double diameter = Math.Max(2, Math.Min(40, _strokeThickness));
        EllipseStrokePreview.Width = diameter;
        EllipseStrokePreview.Height = diameter;
        var col = Color.FromArgb((byte)(255 * _strokeOpacity), _currentColor.R, _currentColor.G, _currentColor.B);
        EllipseStrokePreview.Fill = new SolidColorBrush(col);
    }

    private void OnColorSwatchClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        Button[] swatches = [SwatchRed, SwatchCoral, SwatchOrange, SwatchAmber, SwatchLime, SwatchGreen, SwatchCyan, SwatchBlue, SwatchPurple, SwatchPink, SwatchWhite, SwatchBlack];
        foreach (var s in swatches)
        {
            if (s != null) s.Tag = null;
        }

        btn.Tag = "Selected";

        if (btn.Background is SolidColorBrush scb)
        {
            _currentColor = scb.Color;
            UpdateStrokePreview();
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

        var swatches = new[] { SwatchRed, SwatchCoral, SwatchOrange, SwatchAmber, SwatchLime, SwatchGreen, SwatchCyan, SwatchBlue, SwatchPurple, SwatchPink, SwatchWhite, SwatchBlack };
        foreach (var s in swatches)
        {
            if (s != null) s.Tag = null;
        }

        UpdateStrokePreview();
        PopupColorPicker.IsOpen = false;
    }

    private void OnResetBadgeCounterClicked(object sender, RoutedEventArgs e)
    {
        _stepBadgeCounter = 1;
        if (TxtNextBadgeNumber != null) TxtNextBadgeNumber.Text = "Badge #1";
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

        if (_currentTool == AnnotationTool.Crop)
        {
            return;
        }

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
            MoveElementTo(_selectedElement, _elementOriginalBounds, dx, dy);
            UpdateSelectionOverlay();
            SetDirty(true);
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
            if (_selectedElement != null)
            {
                var finalBounds = GetElementBounds(_selectedElement);
                if (finalBounds != _elementOriginalBounds)
                {
                    _undoStack.Push(new ElementBoundsAction(_selectedElement, _elementOriginalBounds, finalBounds, this));
                    UpdateUndoRedoState();
                }
            }
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
                SetDirty(true);
            }
            else
            {
                DrawingCanvas.Children.Remove(poly);
            }
        }
        else if (dist > 4.0)
        {
            _undoStack.Push(_currentVisual);
            _redoStack.Clear();
            UpdateUndoRedoState();
            SelectElement(_currentVisual);
            SetDirty(true);
        }
        else
        {
            DrawingCanvas.Children.Remove(_currentVisual);
        }

        _currentVisual = null;
    }

    private FrameworkElement? CreateDrawingVisual(AnnotationTool tool, Point start)
    {
        var effectiveColor = Color.FromArgb((byte)(255 * _strokeOpacity), _currentColor.R, _currentColor.G, _currentColor.B);
        var brush = new SolidColorBrush(effectiveColor);

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

            case AnnotationTool.Highlight:
                var highlightRect = new Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(0x55, _currentColor.R, _currentColor.G, _currentColor.B)),
                    Stroke = null,
                    RadiusX = 3,
                    RadiusY = 3
                };
                Canvas.SetLeft(highlightRect, start.X);
                Canvas.SetTop(highlightRect, start.Y);
                return highlightRect;

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
        double diameter = Math.Max(24, _strokeThickness * 4);
        var badge = new Grid
        {
            Width = diameter,
            Height = diameter,
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
            FontSize = Math.Max(11, diameter * 0.45),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        badge.Children.Add(circle);
        badge.Children.Add(text);

        Canvas.SetLeft(badge, Math.Max(0, pos.X - diameter / 2));
        Canvas.SetTop(badge, Math.Max(0, pos.Y - diameter / 2));

        DrawingCanvas.Children.Add(badge);
        _undoStack.Push(badge);
        _redoStack.Clear();
        UpdateUndoRedoState();
        SelectElement(badge);
        SetDirty(true);

        _stepBadgeCounter++;
        if (TxtNextBadgeNumber != null) TxtNextBadgeNumber.Text = $"Badge #{_stepBadgeCounter}";
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
                Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x18, 0x19, 0x20)),
                BorderBrush = new SolidColorBrush(_currentColor),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Child = new TextBlock
                {
                    Text = textBox.Text,
                    Foreground = new SolidColorBrush(_currentColor),
                    FontSize = Math.Max(12, _strokeThickness * 3.0),
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
            SetDirty(true);
        }
    }

    internal void SelectElement(FrameworkElement? element)
    {
        if (_selectedElement != null)
        {
            _selectedElement.Opacity = 1.0;
        }

        _selectedElement = element;
        if (_selectedElement != null)
        {
            if (BtnDeleteSelected != null) BtnDeleteSelected.IsEnabled = true;
            UpdateSelectionOverlay();
        }
        else
        {
            if (BtnDeleteSelected != null) BtnDeleteSelected.IsEnabled = false;
            if (SelectionOverlayCanvas != null) SelectionOverlayCanvas.Visibility = Visibility.Collapsed;
        }
    }

    internal Rect GetElementBounds(FrameworkElement fe)
    {
        if (fe is Polyline poly)
        {
            if (poly.Points.Count == 0) return Rect.Empty;
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var pt in poly.Points)
            {
                if (pt.X < minX) minX = pt.X;
                if (pt.X > maxX) maxX = pt.X;
                if (pt.Y < minY) minY = pt.Y;
                if (pt.Y > maxY) maxY = pt.Y;
            }
            return new Rect(minX, minY, Math.Max(16, maxX - minX), Math.Max(16, maxY - minY));
        }

        if (fe is Canvas c && c.Children.Count >= 2 && c.Children[0] is Line line)
        {
            double minX = Math.Min(line.X1, line.X2);
            double maxX = Math.Max(line.X1, line.X2);
            double minY = Math.Min(line.Y1, line.Y2);
            double maxY = Math.Max(line.Y1, line.Y2);
            return new Rect(minX, minY, Math.Max(16, maxX - minX), Math.Max(16, maxY - minY));
        }

        double left = Canvas.GetLeft(fe);
        double top = Canvas.GetTop(fe);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;

        double w = !double.IsNaN(fe.Width) && fe.Width > 0 ? fe.Width : (fe.ActualWidth > 0 ? fe.ActualWidth : 40);
        double h = !double.IsNaN(fe.Height) && fe.Height > 0 ? fe.Height : (fe.ActualHeight > 0 ? fe.ActualHeight : 40);

        return new Rect(left, top, w, h);
    }

    private void UpdateSelectionOverlay()
    {
        if (_selectedElement == null || SelectionOverlayCanvas == null)
        {
            if (SelectionOverlayCanvas != null) SelectionOverlayCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        var bounds = GetElementBounds(_selectedElement);
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
        {
            SelectionOverlayCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        SelectionOverlayCanvas.Width = DrawingCanvas.Width;
        SelectionOverlayCanvas.Height = DrawingCanvas.Height;
        SelectionOverlayCanvas.Visibility = Visibility.Visible;

        Canvas.SetLeft(SelectionBorder, bounds.X);
        Canvas.SetTop(SelectionBorder, bounds.Y);
        SelectionBorder.Width = bounds.Width;
        SelectionBorder.Height = bounds.Height;

        double hOffset = 5.0; // half of 10px handle
        // Corners
        Canvas.SetLeft(HandleTL, bounds.Left - hOffset);
        Canvas.SetTop(HandleTL, bounds.Top - hOffset);

        Canvas.SetLeft(HandleTR, bounds.Right - hOffset);
        Canvas.SetTop(HandleTR, bounds.Top - hOffset);

        Canvas.SetLeft(HandleBL, bounds.Left - hOffset);
        Canvas.SetTop(HandleBL, bounds.Bottom - hOffset);

        Canvas.SetLeft(HandleBR, bounds.Right - hOffset);
        Canvas.SetTop(HandleBR, bounds.Bottom - hOffset);

        // Edges
        Canvas.SetLeft(HandleTC, bounds.Left + bounds.Width / 2.0 - hOffset);
        Canvas.SetTop(HandleTC, bounds.Top - hOffset);

        Canvas.SetLeft(HandleBC, bounds.Left + bounds.Width / 2.0 - hOffset);
        Canvas.SetTop(HandleBC, bounds.Bottom - hOffset);

        Canvas.SetLeft(HandleML, bounds.Left - hOffset);
        Canvas.SetTop(HandleML, bounds.Top + bounds.Height / 2.0 - hOffset);

        Canvas.SetLeft(HandleMR, bounds.Right - hOffset);
        Canvas.SetTop(HandleMR, bounds.Top + bounds.Height / 2.0 - hOffset);
    }

    private FrameworkElement? FindAnnotationElementUnderMouse(Point pos)
    {
        for (int i = DrawingCanvas.Children.Count - 1; i >= 0; i--)
        {
            var child = DrawingCanvas.Children[i];
            if (child == BaseImageElement || child == _activeWatermarkElement) continue;

            if (child is FrameworkElement fe)
            {
                var bounds = GetElementBounds(fe);
                var inflated = bounds;
                inflated.Inflate(8, 8);
                if (inflated.Contains(pos))
                {
                    return fe;
                }
            }
        }
        return null;
    }

    private void OnSelectionBorderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_selectedElement == null) return;
        _isDraggingElement = true;
        _elementDragStart = e.GetPosition(DrawingCanvas);
        _elementOriginalBounds = GetElementBounds(_selectedElement);

        if (_selectedElement is Polyline poly)
        {
            _origPolyPoints = poly.Points.ToList();
        }
        else if (_selectedElement is Canvas c && c.Children.Count >= 2 && c.Children[0] is Line line && c.Children[1] is Polygon head)
        {
            _origArrowLine = (line.X1, line.Y1, line.X2, line.Y2);
            _origHeadPoints = head.Points.ToList();
        }

        SelectionBorder.CaptureMouse();
        e.Handled = true;
    }

    private void OnSelectionBorderMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingElement && _selectedElement != null)
        {
            var cur = e.GetPosition(DrawingCanvas);
            double dx = cur.X - _elementDragStart.X;
            double dy = cur.Y - _elementDragStart.Y;
            MoveElementTo(_selectedElement, _elementOriginalBounds, dx, dy);
            UpdateSelectionOverlay();
            SetDirty(true);
            e.Handled = true;
        }
    }

    private void OnSelectionBorderMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingElement)
        {
            _isDraggingElement = false;
            SelectionBorder.ReleaseMouseCapture();
            if (_selectedElement != null)
            {
                var finalBounds = GetElementBounds(_selectedElement);
                if (finalBounds != _elementOriginalBounds)
                {
                    _undoStack.Push(new ElementBoundsAction(_selectedElement, _elementOriginalBounds, finalBounds, this));
                    UpdateUndoRedoState();
                }
            }
            e.Handled = true;
        }
    }

    private void OnHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_selectedElement == null || sender is not FrameworkElement fe || fe.Tag is not string tagStr) return;
        if (Enum.TryParse<ResizeHandle>(tagStr, out var handle))
        {
            _activeResizeHandle = handle;
            _resizeStartMouse = e.GetPosition(DrawingCanvas);
            _resizeStartBounds = GetElementBounds(_selectedElement);

            if (_selectedElement is Polyline poly)
            {
                _origPolyPoints = poly.Points.ToList();
            }
            else if (_selectedElement is Canvas c && c.Children.Count >= 2 && c.Children[0] is Line line && c.Children[1] is Polygon head)
            {
                _origArrowLine = (line.X1, line.Y1, line.X2, line.Y2);
                _origHeadPoints = head.Points.ToList();
            }

            fe.CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnHandleMouseMove(object sender, MouseEventArgs e)
    {
        if (_activeResizeHandle != ResizeHandle.None && _selectedElement != null)
        {
            var cur = e.GetPosition(DrawingCanvas);
            double dx = cur.X - _resizeStartMouse.X;
            double dy = cur.Y - _resizeStartMouse.Y;
            var newBounds = CalculateResizedBounds(_resizeStartBounds, _activeResizeHandle, dx, dy);
            ResizeElementTo(_selectedElement, _resizeStartBounds, newBounds);
            UpdateSelectionOverlay();
            SetDirty(true);
            e.Handled = true;
        }
    }

    private void OnHandleMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_activeResizeHandle != ResizeHandle.None)
        {
            if (sender is FrameworkElement fe) fe.ReleaseMouseCapture();
            _activeResizeHandle = ResizeHandle.None;
            if (_selectedElement != null)
            {
                var finalBounds = GetElementBounds(_selectedElement);
                if (finalBounds != _resizeStartBounds)
                {
                    _undoStack.Push(new ElementBoundsAction(_selectedElement, _resizeStartBounds, finalBounds, this));
                    UpdateUndoRedoState();
                }
            }
            e.Handled = true;
        }
    }

    private void MoveElementTo(FrameworkElement fe, Rect origBounds, double dx, double dy)
    {
        if (fe is Polyline poly)
        {
            var basePoints = (_origPolyPoints != null && _origPolyPoints.Count == poly.Points.Count)
                ? _origPolyPoints
                : poly.Points.ToList();

            for (int i = 0; i < poly.Points.Count; i++)
            {
                poly.Points[i] = new Point(basePoints[i].X + dx, basePoints[i].Y + dy);
            }
            return;
        }

        if (fe is Canvas c && c.Children.Count >= 2 && c.Children[0] is Line line && c.Children[1] is Polygon head)
        {
            var origLine = _origArrowLine != default ? _origArrowLine : (line.X1, line.Y1, line.X2, line.Y2);
            var headPoints = (_origHeadPoints != null && _origHeadPoints.Count == head.Points.Count)
                ? _origHeadPoints
                : head.Points.ToList();

            line.X1 = origLine.X1 + dx;
            line.Y1 = origLine.Y1 + dy;
            line.X2 = origLine.X2 + dx;
            line.Y2 = origLine.Y2 + dy;
            for (int i = 0; i < head.Points.Count; i++)
            {
                head.Points[i] = new Point(headPoints[i].X + dx, headPoints[i].Y + dy);
            }
            return;
        }

        Canvas.SetLeft(fe, origBounds.X + dx);
        Canvas.SetTop(fe, origBounds.Y + dy);

        if (fe is Rectangle r && r.Fill is VisualBrush vb)
        {
            vb.Viewbox = new Rect(origBounds.X + dx, origBounds.Y + dy, origBounds.Width, origBounds.Height);
        }
    }

    private void ResizeElementTo(FrameworkElement fe, Rect origBounds, Rect newBounds)
    {
        if (fe is Polyline poly)
        {
            var basePoints = (_origPolyPoints != null && _origPolyPoints.Count == poly.Points.Count)
                ? _origPolyPoints
                : poly.Points.ToList();

            double sx = newBounds.Width / Math.Max(1.0, origBounds.Width);
            double sy = newBounds.Height / Math.Max(1.0, origBounds.Height);
            for (int i = 0; i < poly.Points.Count; i++)
            {
                double relX = basePoints[i].X - origBounds.Left;
                double relY = basePoints[i].Y - origBounds.Top;
                poly.Points[i] = new Point(newBounds.Left + relX * sx, newBounds.Top + relY * sy);
            }
            return;
        }

        if (fe is Canvas c && c.Children.Count >= 2 && c.Children[0] is Line line && c.Children[1] is Polygon head)
        {
            var origLine = _origArrowLine != default ? _origArrowLine : (line.X1, line.Y1, line.X2, line.Y2);
            double sx = newBounds.Width / Math.Max(1.0, origBounds.Width);
            double sy = newBounds.Height / Math.Max(1.0, origBounds.Height);
            line.X1 = newBounds.Left + (origLine.X1 - origBounds.Left) * sx;
            line.Y1 = newBounds.Top + (origLine.Y1 - origBounds.Top) * sy;
            line.X2 = newBounds.Left + (origLine.X2 - origBounds.Left) * sx;
            line.Y2 = newBounds.Top + (origLine.Y2 - origBounds.Top) * sy;

            double angle = Math.Atan2(line.Y2 - line.Y1, line.X2 - line.X1);
            double headLen = Math.Max(12, _strokeThickness * 3.5);
            double headAngle = Math.PI / 6;

            Point p1 = new(line.X2, line.Y2);
            Point p2 = new(line.X2 - headLen * Math.Cos(angle - headAngle), line.Y2 - headLen * Math.Sin(angle - headAngle));
            Point p3 = new(line.X2 - headLen * Math.Cos(angle + headAngle), line.Y2 - headLen * Math.Sin(angle + headAngle));
            head.Points = [p1, p2, p3];
            return;
        }

        Canvas.SetLeft(fe, newBounds.Left);
        Canvas.SetTop(fe, newBounds.Top);
        fe.Width = Math.Max(10, newBounds.Width);
        fe.Height = Math.Max(10, newBounds.Height);

        if (fe is Rectangle r && r.Fill is VisualBrush vb)
        {
            vb.Viewbox = new Rect(newBounds.Left, newBounds.Top, newBounds.Width, newBounds.Height);
        }
    }

    private static Rect CalculateResizedBounds(Rect orig, ResizeHandle handle, double dx, double dy)
    {
        double minSize = 15;
        double l = orig.Left;
        double t = orig.Top;
        double w = orig.Width;
        double h = orig.Height;

        switch (handle)
        {
            case ResizeHandle.TopLeft:
                w = Math.Max(minSize, orig.Width - dx);
                h = Math.Max(minSize, orig.Height - dy);
                l = orig.Right - w;
                t = orig.Bottom - h;
                break;
            case ResizeHandle.Top:
                h = Math.Max(minSize, orig.Height - dy);
                t = orig.Bottom - h;
                break;
            case ResizeHandle.TopRight:
                w = Math.Max(minSize, orig.Width + dx);
                h = Math.Max(minSize, orig.Height - dy);
                t = orig.Bottom - h;
                break;
            case ResizeHandle.Right:
                w = Math.Max(minSize, orig.Width + dx);
                break;
            case ResizeHandle.BottomRight:
                w = Math.Max(minSize, orig.Width + dx);
                h = Math.Max(minSize, orig.Height + dy);
                break;
            case ResizeHandle.Bottom:
                h = Math.Max(minSize, orig.Height + dy);
                break;
            case ResizeHandle.BottomLeft:
                w = Math.Max(minSize, orig.Width - dx);
                h = Math.Max(minSize, orig.Height + dy);
                l = orig.Right - w;
                break;
            case ResizeHandle.Left:
                w = Math.Max(minSize, orig.Width - dx);
                l = orig.Right - w;
                break;
        }

        return new Rect(l, t, w, h);
    }

    internal void ApplyElementBounds(FrameworkElement fe, Rect fromBounds, Rect toBounds)
    {
        ResizeElementTo(fe, fromBounds, toBounds);
        UpdateSelectionOverlay();
        SetDirty(true);
    }

    private void DeleteSelectedElement()
    {
        if (_selectedElement == null) return;

        var el = _selectedElement;
        DrawingCanvas.Children.Remove(el);
        _undoStack.Push(new DeletedAnnotationAction(el, DrawingCanvas));
        SelectElement(null);
        UpdateUndoRedoState();
        SetDirty(true);
    }

    #endregion

    #region Interactive Freeform Cropper Overlay

    private enum CropDragMode
    {
        None,
        Move,
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left
    }

    private CropDragMode _cropDragMode = CropDragMode.None;
    private Point _cropDragStartPoint;
    private Rect _cropInitialRect;
    private Rect _cropCurrentRect;

    public void ShowCropOverlay()
    {
        if (BaseImageElement.Source is not BitmapSource src) return;

        CropLayerCanvas.Visibility = Visibility.Visible;
        CropLayerCanvas.Width = DrawingCanvas.Width;
        CropLayerCanvas.Height = DrawingCanvas.Height;

        if (_cropCurrentRect.Width <= 0 || _cropCurrentRect.Height <= 0 ||
            _cropCurrentRect.Width > DrawingCanvas.Width || _cropCurrentRect.Height > DrawingCanvas.Height)
        {
            double padX = Math.Max(20, DrawingCanvas.Width * 0.08);
            double padY = Math.Max(20, DrawingCanvas.Height * 0.08);
            _cropCurrentRect = new Rect(padX, padY, Math.Max(40, DrawingCanvas.Width - 2 * padX), Math.Max(40, DrawingCanvas.Height - 2 * padY));
        }

        UpdateCropOverlayVisuals();
        if (TxtStatusMessage != null) TxtStatusMessage.Text = "Crop: Drag box or handles to size freely. Press Enter or Apply to crop.";
    }

    public void HideCropOverlay()
    {
        if (CropLayerCanvas != null)
        {
            CropLayerCanvas.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateCropOverlayVisuals()
    {
        if (CropLayerCanvas == null || CropBoxBorder == null) return;

        double canvasW = DrawingCanvas.Width;
        double canvasH = DrawingCanvas.Height;

        // Clamp crop rect to canvas bounds
        double x = Math.Max(0, Math.Min(_cropCurrentRect.X, canvasW - 20));
        double y = Math.Max(0, Math.Min(_cropCurrentRect.Y, canvasH - 20));
        double w = Math.Max(20, Math.Min(_cropCurrentRect.Width, canvasW - x));
        double h = Math.Max(20, Math.Min(_cropCurrentRect.Height, canvasH - y));
        _cropCurrentRect = new Rect(x, y, w, h);

        // Crop box
        Canvas.SetLeft(CropBoxBorder, x);
        Canvas.SetTop(CropBoxBorder, y);
        CropBoxBorder.Width = w;
        CropBoxBorder.Height = h;

        // 4 Shrouds
        Canvas.SetLeft(CropShroudTop, 0);
        Canvas.SetTop(CropShroudTop, 0);
        CropShroudTop.Width = canvasW;
        CropShroudTop.Height = y;

        Canvas.SetLeft(CropShroudBottom, 0);
        Canvas.SetTop(CropShroudBottom, y + h);
        CropShroudBottom.Width = canvasW;
        CropShroudBottom.Height = Math.Max(0, canvasH - (y + h));

        Canvas.SetLeft(CropShroudLeft, 0);
        Canvas.SetTop(CropShroudLeft, y);
        CropShroudLeft.Width = x;
        CropShroudLeft.Height = h;

        Canvas.SetLeft(CropShroudRight, x + w);
        Canvas.SetTop(CropShroudRight, y);
        CropShroudRight.Width = Math.Max(0, canvasW - (x + w));
        CropShroudRight.Height = h;

        // 8 Handles
        Canvas.SetLeft(CropHandleTL, x - 7);
        Canvas.SetTop(CropHandleTL, y - 7);

        Canvas.SetLeft(CropHandleTR, x + w - 7);
        Canvas.SetTop(CropHandleTR, y - 7);

        Canvas.SetLeft(CropHandleBL, x - 7);
        Canvas.SetTop(CropHandleBL, y + h - 7);

        Canvas.SetLeft(CropHandleBR, x + w - 7);
        Canvas.SetTop(CropHandleBR, y + h - 7);

        Canvas.SetLeft(CropHandleT, x + w / 2 - 12);
        Canvas.SetTop(CropHandleT, y - 4);

        Canvas.SetLeft(CropHandleB, x + w / 2 - 12);
        Canvas.SetTop(CropHandleB, y + h - 4);

        Canvas.SetLeft(CropHandleL, x - 4);
        Canvas.SetTop(CropHandleL, y + h / 2 - 12);

        Canvas.SetLeft(CropHandleR, x + w - 4);
        Canvas.SetTop(CropHandleR, y + h / 2 - 12);

        // Floating Action Bar
        double barLeft = Math.Max(10, Math.Min(x + (w - 270) / 2, canvasW - 280));
        double barTop = (y + h + 12 + 40 <= canvasH) ? y + h + 12 : Math.Max(10, y - 46);
        Canvas.SetLeft(CropActionBar, barLeft);
        Canvas.SetTop(CropActionBar, barTop);

        if (TxtCropDims != null)
        {
            TxtCropDims.Text = $"{(int)w} × {(int)h} px";
        }
    }

    private void OnCropLayerMouseDown(object sender, MouseButtonEventArgs e)
    {
        var pt = e.GetPosition(CropLayerCanvas);
        _cropDragStartPoint = pt;
        _cropInitialRect = _cropCurrentRect;

        var source = e.OriginalSource as FrameworkElement;
        if (source == CropHandleTL) _cropDragMode = CropDragMode.TopLeft;
        else if (source == CropHandleTR) _cropDragMode = CropDragMode.TopRight;
        else if (source == CropHandleBL) _cropDragMode = CropDragMode.BottomLeft;
        else if (source == CropHandleBR) _cropDragMode = CropDragMode.BottomRight;
        else if (source == CropHandleT) _cropDragMode = CropDragMode.Top;
        else if (source == CropHandleB) _cropDragMode = CropDragMode.Bottom;
        else if (source == CropHandleL) _cropDragMode = CropDragMode.Left;
        else if (source == CropHandleR) _cropDragMode = CropDragMode.Right;
        else if (IsDescendantOf(source, CropBoxBorder)) _cropDragMode = CropDragMode.Move;
        else
        {
            // Click outside initiates new crop box from this point
            _cropCurrentRect = new Rect(pt.X, pt.Y, 20, 20);
            _cropInitialRect = _cropCurrentRect;
            _cropDragMode = CropDragMode.BottomRight;
            UpdateCropOverlayVisuals();
        }

        CropLayerCanvas.CaptureMouse();
        e.Handled = true;
    }

    private static bool IsDescendantOf(FrameworkElement? element, FrameworkElement target)
    {
        while (element != null)
        {
            if (element == target) return true;
            element = VisualTreeHelper.GetParent(element) as FrameworkElement;
        }
        return false;
    }

    private void OnCropLayerMouseMove(object sender, MouseEventArgs e)
    {
        if (_cropDragMode == CropDragMode.None) return;

        var pt = e.GetPosition(CropLayerCanvas);
        double dx = pt.X - _cropDragStartPoint.X;
        double dy = pt.Y - _cropDragStartPoint.Y;

        double newX = _cropInitialRect.X;
        double newY = _cropInitialRect.Y;
        double newW = _cropInitialRect.Width;
        double newH = _cropInitialRect.Height;

        switch (_cropDragMode)
        {
            case CropDragMode.Move:
                newX += dx;
                newY += dy;
                break;
            case CropDragMode.TopLeft:
                newX += dx;
                newY += dy;
                newW -= dx;
                newH -= dy;
                break;
            case CropDragMode.TopRight:
                newY += dy;
                newW += dx;
                newH -= dy;
                break;
            case CropDragMode.BottomLeft:
                newX += dx;
                newW -= dx;
                newH += dy;
                break;
            case CropDragMode.BottomRight:
                newW += dx;
                newH += dy;
                break;
            case CropDragMode.Top:
                newY += dy;
                newH -= dy;
                break;
            case CropDragMode.Bottom:
                newH += dy;
                break;
            case CropDragMode.Left:
                newX += dx;
                newW -= dx;
                break;
            case CropDragMode.Right:
                newW += dx;
                break;
        }

        if (newW < 20) { newW = 20; newX = _cropCurrentRect.X; }
        if (newH < 20) { newH = 20; newY = _cropCurrentRect.Y; }

        _cropCurrentRect = new Rect(newX, newY, newW, newH);
        UpdateCropOverlayVisuals();
        e.Handled = true;
    }

    private void OnCropLayerMouseUp(object sender, MouseButtonEventArgs e)
    {
        _cropDragMode = CropDragMode.None;
        CropLayerCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    internal void OnApplyCropClicked(object sender, RoutedEventArgs e)
    {
        ApplyCropSelectionInternal(_cropCurrentRect);
        HideCropOverlay();
        SetTool(AnnotationTool.Select);
    }

    private void OnCancelCropClicked(object sender, RoutedEventArgs e)
    {
        HideCropOverlay();
        SetTool(AnnotationTool.Select);
        if (TxtStatusMessage != null) TxtStatusMessage.Text = "Crop cancelled.";
    }

    private void ApplyCropSelectionInternal(Rect cropRect)
    {
        if (BaseImageElement.Source is not BitmapSource src) return;

        int cx = Math.Max(0, (int)cropRect.X);
        int cy = Math.Max(0, (int)cropRect.Y);
        int cw = Math.Min(src.PixelWidth - cx, (int)cropRect.Width);
        int ch = Math.Min(src.PixelHeight - cy, (int)cropRect.Height);

        if (cw < 10 || ch < 10) return;

        try
        {
            var prevBitmap = _originalBitmap ?? src;
            var cropped = new CroppedBitmap(src, new Int32Rect(cx, cy, cw, ch));

            // Push undo state!
            _undoStack.Push(new BitmapSnapshotAction(prevBitmap, cropped, $"Crop ({cw} × {ch})"));
            _redoStack.Clear();
            UpdateUndoRedoState();

            _originalBitmap = cropped;
            _baseTunedBitmap = cropped;
            ApplyBitmap(cropped, IOPath.GetFileName(_currentImagePath ?? "Cropped"), clearAnnotations: false);
            SetDirty(true);
            if (TxtStatusMessage != null) TxtStatusMessage.Text = $"Cropped image to {cw} × {ch}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not crop image: {ex.Message}", "Crop Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyCropSelection(FrameworkElement cropMarquee)
    {
        double x = Canvas.GetLeft(cropMarquee);
        double y = Canvas.GetTop(cropMarquee);
        double w = cropMarquee.Width;
        double h = cropMarquee.Height;
        ApplyCropSelectionInternal(new Rect(x, y, w, h));
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
        if (TxtValBlur != null) TxtValBlur.Text = $"{(int)blur} px";

        var adjusted = GenerateAdjustedBitmap(_originalBitmap, brightness, contrast, saturation);
        BaseImageElement.Source = adjusted;
        _baseTunedBitmap = adjusted;
        BaseImageElement.Effect = blur > 0.5 ? new BlurEffect { Radius = blur } : null;

        bool hasAdjustments = brightness != 0 || contrast != 0 || saturation != 0 || blur > 0.5;
        if (hasAdjustments)
        {
            SetDirty(true);
        }
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

            double dr = cFactor * (r - 128) + 128 + brightness;
            double dg = cFactor * (g - 128) + 128 + brightness;
            double db = cFactor * (b - 128) + 128 + brightness;

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

    #region Watermark Studio (Disabled by default!)

    private void OnWatermarkToggleChanged(object sender, RoutedEventArgs e)
    {
        _isWatermarkActive = ChkEnableWatermark?.IsChecked == true;

        if (SecWatermarkControls != null)
        {
            SecWatermarkControls.IsEnabled = _isWatermarkActive;
            SecWatermarkControls.Opacity = _isWatermarkActive ? 1.0 : 0.5;
        }

        UpdateWatermarkPreview();
        SetDirty(true);
    }

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
            SetDirty(true);
        }
    }

    private void UpdateWatermarkPreview()
    {
        // 1. If watermark is NOT active, remove visual and do not show anything!
        if (!_isWatermarkActive)
        {
            if (_activeWatermarkElement != null && DrawingCanvas != null && DrawingCanvas.Children.Contains(_activeWatermarkElement))
            {
                DrawingCanvas.Children.Remove(_activeWatermarkElement);
                _activeWatermarkElement = null;
            }
            return;
        }

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
            string watermarkText = TxtWatermarkInput?.Text ?? "CONFIDENTIAL";
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

        ShowCropOverlay();

        double canvasW = DrawingCanvas.Width;
        double canvasH = DrawingCanvas.Height;
        double targetW = canvasW * 0.8;
        double targetH = canvasH * 0.8;

        switch (tag)
        {
            case "1:1":
                double side = Math.Min(targetW, targetH);
                targetW = side;
                targetH = side;
                break;
            case "16:9":
                targetH = targetW * (9.0 / 16.0);
                if (targetH > canvasH * 0.9)
                {
                    targetH = canvasH * 0.9;
                    targetW = targetH * (16.0 / 9.0);
                }
                break;
            case "4:3":
                targetH = targetW * (3.0 / 4.0);
                if (targetH > canvasH * 0.9)
                {
                    targetH = canvasH * 0.9;
                    targetW = targetH * (4.0 / 3.0);
                }
                break;
        }

        _cropCurrentRect = new Rect((canvasW - targetW) / 2, (canvasH - targetH) / 2, targetW, targetH);
        UpdateCropOverlayVisuals();
    }

    private void OnRotateCWClicked(object sender, RoutedEventArgs e)
    {
        if (_originalBitmap == null) return;
        var prev = _originalBitmap;
        var transformed = new TransformedBitmap(_originalBitmap, new RotateTransform(90));
        transformed.Freeze();

        _undoStack.Push(new BitmapSnapshotAction(prev, transformed, "Rotate 90° CW"));
        _redoStack.Clear();
        UpdateUndoRedoState();

        _originalBitmap = transformed;
        _baseTunedBitmap = transformed;
        ApplyBitmap(transformed, IOPath.GetFileName(_currentImagePath ?? "Rotated"), clearAnnotations: false);
        SetDirty(true);
    }

    private void OnRotateCCWClicked(object sender, RoutedEventArgs e)
    {
        if (_originalBitmap == null) return;
        var prev = _originalBitmap;
        var transformed = new TransformedBitmap(_originalBitmap, new RotateTransform(270));
        transformed.Freeze();

        _undoStack.Push(new BitmapSnapshotAction(prev, transformed, "Rotate 90° CCW"));
        _redoStack.Clear();
        UpdateUndoRedoState();

        _originalBitmap = transformed;
        _baseTunedBitmap = transformed;
        ApplyBitmap(transformed, IOPath.GetFileName(_currentImagePath ?? "Rotated"), clearAnnotations: false);
        SetDirty(true);
    }

    private void OnFlipHorizontalClicked(object sender, RoutedEventArgs e)
    {
        if (_originalBitmap == null) return;
        var prev = _originalBitmap;
        var transformed = new TransformedBitmap(_originalBitmap, new ScaleTransform(-1, 1, _originalBitmap.PixelWidth / 2.0, 0));
        transformed.Freeze();

        _undoStack.Push(new BitmapSnapshotAction(prev, transformed, "Flip Horizontal"));
        _redoStack.Clear();
        UpdateUndoRedoState();

        _originalBitmap = transformed;
        _baseTunedBitmap = transformed;
        ApplyBitmap(transformed, IOPath.GetFileName(_currentImagePath ?? "Flipped_H"), clearAnnotations: false);
        SetDirty(true);
    }

    private void OnFlipVerticalClicked(object sender, RoutedEventArgs e)
    {
        if (_originalBitmap == null) return;
        var prev = _originalBitmap;
        var transformed = new TransformedBitmap(_originalBitmap, new ScaleTransform(1, -1, 0, _originalBitmap.PixelHeight / 2.0));
        transformed.Freeze();

        _undoStack.Push(new BitmapSnapshotAction(prev, transformed, "Flip Vertical"));
        _redoStack.Clear();
        UpdateUndoRedoState();

        _originalBitmap = transformed;
        _baseTunedBitmap = transformed;
        ApplyBitmap(transformed, IOPath.GetFileName(_currentImagePath ?? "Flipped_V"), clearAnnotations: false);
        SetDirty(true);
    }

    #endregion

    #region Undo / Redo

    internal void OnUndoClicked(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0) return;

        var item = _undoStack.Pop();
        if (item is BitmapSnapshotAction bmpAction)
        {
            _originalBitmap = bmpAction.OldBitmap;
            _baseTunedBitmap = bmpAction.OldBitmap;
            ApplyBitmap(bmpAction.OldBitmap, IOPath.GetFileName(_currentImagePath ?? "Image"), clearAnnotations: false);
            _redoStack.Push(bmpAction);
            UpdateUndoRedoState();
            SetDirty(true);
            if (TxtStatusMessage != null) TxtStatusMessage.Text = $"Undid {bmpAction.Description}";
            return;
        }
        else if (item is DeletedAnnotationAction del)
        {
            del.Undo();
        }
        else if (item is ElementBoundsAction boundsAction)
        {
            boundsAction.Undo();
        }
        else
        {
            DrawingCanvas.Children.Remove(item);
        }
        _redoStack.Push(item);
        UpdateUndoRedoState();
        SelectElement(null);
        SetDirty(true);
        if (TxtStatusMessage != null) TxtStatusMessage.Text = "Undid last annotation";
    }

    internal void OnRedoClicked(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;

        var item = _redoStack.Pop();
        if (item is BitmapSnapshotAction bmpAction)
        {
            _originalBitmap = bmpAction.NewBitmap;
            _baseTunedBitmap = bmpAction.NewBitmap;
            ApplyBitmap(bmpAction.NewBitmap, IOPath.GetFileName(_currentImagePath ?? "Image"), clearAnnotations: false);
            _undoStack.Push(bmpAction);
            UpdateUndoRedoState();
            SetDirty(true);
            if (TxtStatusMessage != null) TxtStatusMessage.Text = $"Redid {bmpAction.Description}";
            return;
        }
        else if (item is DeletedAnnotationAction del)
        {
            del.Redo();
        }
        else if (item is ElementBoundsAction boundsAction)
        {
            boundsAction.Redo();
        }
        else
        {
            DrawingCanvas.Children.Add(item);
        }
        _undoStack.Push(item);
        UpdateUndoRedoState();
        SelectElement(item as FrameworkElement);
        SetDirty(true);
        if (TxtStatusMessage != null) TxtStatusMessage.Text = "Redid annotation";
    }

    private void OnDeleteSelectedClicked(object sender, RoutedEventArgs e) => DeleteSelectedElement();

    private void OnClearAllClicked(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Clear all markings and annotations?", "Clear Canvas", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            ClearCanvasAnnotations();
            SetDirty(true);
        }
    }

    private void UpdateUndoRedoState()
    {
        if (BtnUndo != null)
        {
            BtnUndo.IsEnabled = _undoStack.Count > 0;
            BtnUndo.ToolTip = _undoStack.Count > 0 ? $"Undo (Ctrl+Z) [{_undoStack.Count}]" : "Undo (Ctrl+Z)";
        }
        if (BtnRedo != null)
        {
            BtnRedo.IsEnabled = _redoStack.Count > 0;
            BtnRedo.ToolTip = _redoStack.Count > 0 ? $"Redo (Ctrl+Y) [{_redoStack.Count}]" : "Redo (Ctrl+Y)";
        }
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

        double availW = ImageScrollViewer?.ActualWidth > 0 ? ImageScrollViewer.ActualWidth - 72 : 800;
        double availH = ImageScrollViewer?.ActualHeight > 0 ? ImageScrollViewer.ActualHeight - 72 : 600;

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
        SetDirty(false);
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
            SetDirty(false);
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
        if (CropLayerCanvas != null && CropLayerCanvas.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Enter)
            {
                OnApplyCropClicked(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                OnCancelCropClicked(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
        }

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
        else if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OnQuickSaveClicked(sender, e);
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
                case Key.H: SetTool(AnnotationTool.Highlight); e.Handled = true; break;
                case Key.B: SetTool(AnnotationTool.Blur); e.Handled = true; break;
                case Key.X: SetTool(AnnotationTool.Redact); e.Handled = true; break;
                case Key.C: SetTool(AnnotationTool.Crop); e.Handled = true; break;
                case Key.V: SetTool(AnnotationTool.Select); e.Handled = true; break;
            }
        }
    }

    #endregion

    private sealed class BitmapSnapshotAction : UIElement
    {
        public BitmapSource OldBitmap { get; }
        public BitmapSource NewBitmap { get; }
        public string Description { get; }

        public BitmapSnapshotAction(BitmapSource oldBitmap, BitmapSource newBitmap, string description)
        {
            OldBitmap = oldBitmap;
            NewBitmap = newBitmap;
            Description = description;
        }
    }

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

    private sealed class ElementBoundsAction : UIElement
    {
        public FrameworkElement Element { get; }
        public Rect OldBounds { get; }
        public Rect NewBounds { get; }
        public ImageAnnotationWindow Window { get; }

        public ElementBoundsAction(FrameworkElement element, Rect oldBounds, Rect newBounds, ImageAnnotationWindow window)
        {
            Element = element;
            OldBounds = oldBounds;
            NewBounds = newBounds;
            Window = window;
        }

        public void Undo()
        {
            Window.ApplyElementBounds(Element, NewBounds, OldBounds);
        }

        public void Redo()
        {
            Window.ApplyElementBounds(Element, OldBounds, NewBounds);
        }
    }
}
