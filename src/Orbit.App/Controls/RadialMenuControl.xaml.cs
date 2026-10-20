namespace Orbit.App.Controls;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Orbit.Core.Models;
using Orbit.Core.Services;

public partial class RadialMenuControl : UserControl
{
    private const double CenterX = 270;
    private const double CenterY = 270;
    private const double InnerDeadZoneRadius = 48;
    private const double CategoryInnerRadius = 56;
    private const double CategoryOuterRadius = 118;
    private const double ActionInnerRadius = 126;
    private const double ActionOuterRadius = 186;

    private ResolvedCapabilities? _capabilities;
    private OperationCategory? _activeCategory;
    private OperationDefinition? _activeAction;

    public event Action<OperationDefinition>? ActionCommitted;
    public event Action? MenuCancelled;

    private readonly List<CategorySectorVisual> _categoryVisuals = new();
    private readonly List<ActionSectorVisual> _actionVisuals = new();

    public RadialMenuControl()
    {
        InitializeComponent();
        MouseMove += OnRadialMouseMove;
        MouseLeftButtonUp += OnRadialMouseUp;
    }

    public void Populate(ResolvedCapabilities capabilities)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _activeCategory = null;
        _activeAction = null;

        // Update file badge
        if (capabilities.Files.Length > 0)
        {
            var first = capabilities.Files[0];
            TxtFileBadgeName.Text = capabilities.Files.Length == 1 
                ? first.FileName 
                : $"{first.FileName} (+{capabilities.Files.Length - 1} more)";
            
            var sizeMb = (first.FileSize / (1024.0 * 1024.0)).ToString("0.1");
            TxtFileBadgeDetails.Text = $"{first.Family} • {sizeMb} MB";
        }

        RenderCategoryRing();
    }

    private void RenderCategoryRing()
    {
        CategoryWedgesCanvas.Children.Clear();
        _categoryVisuals.Clear();
        ActionWedgesCanvas.Children.Clear();
        _actionVisuals.Clear();

        if (_capabilities == null || _capabilities.AvailableCategories.Length == 0)
        {
            return;
        }

        var categories = _capabilities.AvailableCategories;
        double angleStep = 360.0 / categories.Length;

        for (int i = 0; i < categories.Length; i++)
        {
            var category = categories[i];
            double startAngle = i * angleStep - 90; // Top-oriented
            double endAngle = startAngle + angleStep;

            var sectorPath = CreateArcSector(CenterX, CenterY, CategoryInnerRadius, CategoryOuterRadius, startAngle, endAngle);
            sectorPath.Fill = new SolidColorBrush(Color.FromArgb(0xDD, 0x1E, 0x20, 0x2A));
            sectorPath.Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0x50, 0x55, 0x68));
            sectorPath.StrokeThickness = 1.2;

            CategoryWedgesCanvas.Children.Add(sectorPath);

            // Add label
            double midAngle = (startAngle + endAngle) / 2.0;
            double rad = midAngle * Math.PI / 180.0;
            double labelRadius = (CategoryInnerRadius + CategoryOuterRadius) / 2.0;
            double lx = CenterX + labelRadius * Math.Cos(rad);
            double ly = CenterY + labelRadius * Math.Sin(rad);

            var textBlock = new TextBlock
            {
                Text = GetCategoryShortLabel(category),
                Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD4, 0xE4)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                IsHitTestVisible = false
            };
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(textBlock, lx - textBlock.DesiredSize.Width / 2);
            Canvas.SetTop(textBlock, ly - textBlock.DesiredSize.Height / 2);

            CategoryWedgesCanvas.Children.Add(textBlock);

            _categoryVisuals.Add(new CategorySectorVisual(category, startAngle, endAngle, sectorPath, textBlock));
        }

        // Default to first category if available
        if (categories.Length > 0)
        {
            SelectCategory(categories[0]);
        }
    }

    public void SelectCategory(OperationCategory category)
    {
        _activeCategory = category;
        _activeAction = null;
        HoverInfoCard.Visibility = Visibility.Collapsed;

        TxtHubLabel.Text = category.ToString();
        TxtHubIcon.Text = GetCategoryIcon(category);

        // Highlight selected category visual
        foreach (var visual in _categoryVisuals)
        {
            bool isSelected = visual.Category == category;
            visual.Path.Fill = isSelected 
                ? new SolidColorBrush(Color.FromArgb(0xFF, 0xE6, 0x5C, 0x00)) 
                : new SolidColorBrush(Color.FromArgb(0xDD, 0x1E, 0x20, 0x2A));
            visual.Text.Foreground = isSelected 
                ? Brushes.White 
                : new SolidColorBrush(Color.FromRgb(0xD0, 0xD4, 0xE4));
        }

        RenderActionRing(category);
    }

    private void RenderActionRing(OperationCategory category)
    {
        ActionWedgesCanvas.Children.Clear();
        _actionVisuals.Clear();

        if (_capabilities == null || 
            !_capabilities.CategorizedOperations.TryGetValue(category, out var operations) || 
            operations.Count == 0)
        {
            return;
        }

        var ops = operations.Take(8).ToList();
        double angleStep = 360.0 / ops.Count;

        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            double startAngle = i * angleStep - 90;
            double endAngle = startAngle + angleStep;

            var sectorPath = CreateArcSector(CenterX, CenterY, ActionInnerRadius, ActionOuterRadius, startAngle, endAngle);
            sectorPath.Fill = new SolidColorBrush(Color.FromArgb(0xD5, 0x28, 0x2B, 0x38));
            sectorPath.Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0x4B, 0x50, 0x65));
            sectorPath.StrokeThickness = 1.0;

            ActionWedgesCanvas.Children.Add(sectorPath);

            // Add label
            double midAngle = (startAngle + endAngle) / 2.0;
            double rad = midAngle * Math.PI / 180.0;
            double labelRadius = (ActionInnerRadius + ActionOuterRadius) / 2.0;
            double lx = CenterX + labelRadius * Math.Cos(rad);
            double ly = CenterY + labelRadius * Math.Sin(rad);

            var textBlock = new TextBlock
            {
                Text = op.Label,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xFA)),
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 72,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(textBlock, lx - textBlock.DesiredSize.Width / 2);
            Canvas.SetTop(textBlock, ly - textBlock.DesiredSize.Height / 2);

            ActionWedgesCanvas.Children.Add(textBlock);

            _actionVisuals.Add(new ActionSectorVisual(op, startAngle, endAngle, sectorPath, textBlock));
        }
    }

    private void OnRadialMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        UpdateHoverState(pos.X, pos.Y);
    }

    public void UpdateHoverState(double x, double y)
    {
        double dx = x - CenterX;
        double dy = y - CenterY;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        // Check if inside inner deadzone (Cancel)
        if (distance <= InnerDeadZoneRadius)
        {
            TxtHubLabel.Text = "Cancel";
            TxtHubIcon.Text = "✕";
            CentralHubCircle.Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x00));
            HoverInfoCard.Visibility = Visibility.Collapsed;
            _activeAction = null;
            return;
        }

        CentralHubCircle.Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x1A));

        // Calculate polar angle: [0, 360)
        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI; // [-180, 180]

        // Check Category Ring
        if (distance >= CategoryInnerRadius && distance <= CategoryOuterRadius)
        {
            foreach (var visual in _categoryVisuals)
            {
                if (IsAngleBetween(angle, visual.StartAngle, visual.EndAngle))
                {
                    if (_activeCategory != visual.Category)
                    {
                        SelectCategory(visual.Category);
                    }
                    break;
                }
            }
        }
        // Check Action Ring
        else if (distance >= ActionInnerRadius && distance <= ActionOuterRadius)
        {
            foreach (var visual in _actionVisuals)
            {
                if (IsAngleBetween(angle, visual.StartAngle, visual.EndAngle))
                {
                    HighlightAction(visual);
                    return;
                }
            }
        }
    }

    private void HighlightAction(ActionSectorVisual visual)
    {
        _activeAction = visual.Operation;

        foreach (var v in _actionVisuals)
        {
            bool isTarget = v == visual;
            v.Path.Fill = isTarget 
                ? new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x73, 0x00)) 
                : new SolidColorBrush(Color.FromArgb(0xD5, 0x28, 0x2B, 0x38));
            v.Text.Foreground = isTarget ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xFA));
        }

        // Show hover card
        TxtHoverTitle.Text = visual.Operation.Label;
        TxtHoverDescription.Text = visual.Operation.Description ?? "Execute selected operation";
        HoverInfoCard.Visibility = Visibility.Visible;
    }

    private void OnRadialMouseUp(object sender, MouseButtonEventArgs e)
    {
        CommitCurrentSelection();
    }

    public void CommitCurrentSelection()
    {
        if (_activeAction != null)
        {
            ActionCommitted?.Invoke(_activeAction);
        }
        else
        {
            MenuCancelled?.Invoke();
        }
    }

    public void TriggerCategoryShortcut(char key)
    {
        var category = key switch
        {
            'C' => OperationCategory.Convert,
            'E' => OperationCategory.Edit,
            'A' => OperationCategory.Annotate,
            'O' => OperationCategory.Organize,
            'P' => OperationCategory.Optimize,
            'X' => OperationCategory.Extract,
            'S' => OperationCategory.Secure,
            'I' => OperationCategory.Analyze,
            _ => (OperationCategory?)null
        };

        if (category.HasValue && _capabilities != null && _capabilities.AvailableCategories.Contains(category.Value))
        {
            SelectCategory(category.Value);
        }
    }

    private static bool IsAngleBetween(double angle, double start, double end)
    {
        // Normalize all angles to [0, 360)
        double normAngle = (angle % 360 + 360) % 360;
        double normStart = (start % 360 + 360) % 360;
        double normEnd = (end % 360 + 360) % 360;

        if (normStart < normEnd)
        {
            return normAngle >= normStart && normAngle < normEnd;
        }
        else
        {
            return normAngle >= normStart || normAngle < normEnd;
        }
    }

    private static Path CreateArcSector(double cx, double cy, double innerR, double outerR, double startAngle, double endAngle)
    {
        double startRad = startAngle * Math.PI / 180.0;
        double endRad = endAngle * Math.PI / 180.0;

        var p1 = new Point(cx + innerR * Math.Cos(startRad), cy + innerR * Math.Sin(startRad));
        var p2 = new Point(cx + outerR * Math.Cos(startRad), cy + outerR * Math.Sin(startRad));
        var p3 = new Point(cx + outerR * Math.Cos(endRad), cy + outerR * Math.Sin(endRad));
        var p4 = new Point(cx + innerR * Math.Cos(endRad), cy + innerR * Math.Sin(endRad));

        double angleDelta = Math.Abs(endAngle - startAngle);
        bool isLargeArc = angleDelta > 180.0;

        var figure = new PathFigure { StartPoint = p1, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(p2, true));
        figure.Segments.Add(new ArcSegment(p3, new Size(outerR, outerR), 0, isLargeArc, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(p4, true));
        figure.Segments.Add(new ArcSegment(p1, new Size(innerR, innerR), 0, isLargeArc, SweepDirection.Counterclockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return new Path { Data = geometry };
    }

    private static string GetCategoryShortLabel(OperationCategory cat) => cat switch
    {
        OperationCategory.Convert => "Convert",
        OperationCategory.Edit => "Edit",
        OperationCategory.Annotate => "Annotate",
        OperationCategory.Optimize => "Optimize",
        OperationCategory.Organize => "Organize",
        OperationCategory.Extract => "Extract",
        OperationCategory.Secure => "Secure",
        OperationCategory.Analyze => "Analyze",
        OperationCategory.Create => "Create",
        OperationCategory.Share => "Share",
        _ => cat.ToString()
    };

    private static string GetCategoryIcon(OperationCategory cat) => cat switch
    {
        OperationCategory.Convert => "🔄",
        OperationCategory.Edit => "✂",
        OperationCategory.Annotate => "✏",
        OperationCategory.Optimize => "⚡",
        OperationCategory.Organize => "📁",
        OperationCategory.Extract => "📦",
        OperationCategory.Secure => "🔒",
        OperationCategory.Analyze => "🔍",
        OperationCategory.Create => "➕",
        OperationCategory.Share => "🚀",
        _ => "☰"
    };

    private record CategorySectorVisual(OperationCategory Category, double StartAngle, double EndAngle, Path Path, TextBlock Text);
    private record ActionSectorVisual(OperationDefinition Operation, double StartAngle, double EndAngle, Path Path, TextBlock Text);
}
