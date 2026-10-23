namespace Orbit.App;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Orbit.App.Views;
using Orbit.Core.Models;
using Orbit.Core.Services;

public partial class MainWindow : Window
{
    private readonly IFileIdentifier _fileIdentifier;
    private readonly IOperationRegistry _operationRegistry;
    private readonly ICapabilityResolver _capabilityResolver;
    private readonly IServiceProvider _serviceProvider;

    private string[]? _lastDroppedFiles;
    private List<OperationDefinition> _allOperations = new();
    private List<OperationDefinition> _currentDisplayedOperations = new();

    public MainWindow(
        IFileIdentifier fileIdentifier,
        IOperationRegistry operationRegistry,
        ICapabilityResolver capabilityResolver,
        IServiceProvider serviceProvider)
    {
        InitializeComponent();

        _fileIdentifier = fileIdentifier ?? throw new ArgumentNullException(nameof(fileIdentifier));
        _operationRegistry = operationRegistry ?? throw new ArgumentNullException(nameof(operationRegistry));
        _capabilityResolver = capabilityResolver ?? throw new ArgumentNullException(nameof(capabilityResolver));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        _allOperations = _operationRegistry.GetAll().ToList();
        TxtOperationsCount.Text = $"{_allOperations.Count} operations active across 5 engines";
        _currentDisplayedOperations = _allOperations;
        LstResolvedOperations.ItemsSource = _currentDisplayedOperations;
    }

    #region Search and Filtering

    private void OnSearchOpsTextChanged(object sender, TextChangedEventArgs e)
    {
        var query = TxtSearchOps?.Text?.Trim().ToLowerInvariant() ?? string.Empty;

        var sourceList = (_lastDroppedFiles != null && _lastDroppedFiles.Length > 0)
            ? _currentDisplayedOperations
            : _allOperations;

        if (string.IsNullOrEmpty(query))
        {
            LstResolvedOperations.ItemsSource = sourceList;
            TxtOperationsCount.Text = $"{sourceList.Count} operations available";
        }
        else
        {
            var filtered = sourceList.Where(op =>
                op.Label.ToLowerInvariant().Contains(query) ||
                (op.Description != null && op.Description.ToLowerInvariant().Contains(query)) ||
                op.Category.ToString().ToLowerInvariant().Contains(query) ||
                (op.InputFormats != null && op.InputFormats.Any(ext => ext.ToLowerInvariant().Contains(query)))
            ).ToList();

            LstResolvedOperations.ItemsSource = filtered;
            TxtOperationsCount.Text = $"{filtered.Count} matches found";
        }
    }

    #endregion

    #region Drag and Drop & File Browsing

    private void OnDragOverFiles(object sender, DragEventArgs e)
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

    private void OnDropFiles(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;

        ProcessSelectedFiles(files);
    }

    private void OnBrowseFilesClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Test File(s) for Orbit",
            Filter = "All Supported Files (*.*)|*.*|PDF Documents (*.pdf)|*.pdf|Images (*.png;*.jpg;*.webp)|*.png;*.jpg;*.webp|Archives (*.zip;*.7z;*.tar)|*.zip;*.7z;*.tar|Media (*.mp4;*.mp3)|*.mp4;*.mp3",
            Multiselect = true
        };

        if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
        {
            ProcessSelectedFiles(dlg.FileNames);
        }
    }

    private void ProcessSelectedFiles(string[] files)
    {
        _lastDroppedFiles = files;
        var identities = _fileIdentifier.IdentifyMultiple(files);
        var resolved = _capabilityResolver.Resolve(identities);

        var fileSummary = string.Join(", ", identities.Select(f => $"{f.FileName} ({f.Family})"));
        var categories = string.Join(" • ", resolved.AvailableCategories.Select(c => c.ToString()));
        var totalOps = resolved.CategorizedOperations.Values.Sum(l => l.Count);

        TxtDropSummary.Text = $"Selected {files.Length} file(s): {fileSummary}\nCategories ({resolved.AvailableCategories.Length}): {categories}\nResolved {totalOps} contextual operations (Batch={resolved.IsBatchOperation}, Mixed={resolved.IsMixedSelection})";

        // Show workspace quick action buttons
        DropActionButtons.Visibility = Visibility.Visible;
        bool hasImages = identities.Any(f => f.Family == FileFamily.Image);
        bool hasPdf = identities.Any(f => f.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || f.Family == FileFamily.Document);

        BtnDroppedOpenAnnotator.Visibility = hasImages ? Visibility.Visible : Visibility.Collapsed;
        BtnDroppedOpenPdfStudio.Visibility = hasPdf ? Visibility.Visible : Visibility.Collapsed;
        BtnDroppedOpenCompressor.Visibility = Visibility.Visible;
        BtnDroppedOpenBatch.Visibility = Visibility.Visible;
        BtnDroppedSummonWheel.Visibility = Visibility.Visible;

        // Update list with resolved operations
        var resolvedList = resolved.CategorizedOperations.Values.SelectMany(x => x).DistinctBy(x => x.Id).ToList();
        _currentDisplayedOperations = resolvedList;
        LstResolvedOperations.ItemsSource = resolvedList;
        TxtOperationsCount.Text = $"{resolvedList.Count} contextual operations resolved";
    }

    #endregion

    #region Direct Operation Execution

    private void OnRunOperationClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not OperationDefinition operation) return;

        FileIdentity[]? targetIdentities = null;
        if (_lastDroppedFiles != null && _lastDroppedFiles.Length > 0)
        {
            targetIdentities = _fileIdentifier.IdentifyMultiple(_lastDroppedFiles);
        }

        if (Application.Current is App app)
        {
            app.ExecuteOperationFromAnywhere(operation, targetIdentities);
        }
    }

    private void OnDroppedSummonWheelClicked(object sender, RoutedEventArgs e)
    {
        if (_lastDroppedFiles == null || _lastDroppedFiles.Length == 0) return;

        // Calculate center position of this window on screen
        var pt = PointToScreen(new Point(ActualWidth / 2.0, ActualHeight / 2.0));
        var overlay = _serviceProvider.GetRequiredService<OverlayWindow>();
        overlay.ShowAt((int)pt.X, (int)pt.Y, _lastDroppedFiles);
    }

    #endregion

    #region Dedicated Workspace Navigation

    private void OnOpenAnnotatorClicked(object sender, RoutedEventArgs e)
    {
        var annotator = _serviceProvider.GetRequiredService<ImageAnnotationWindow>();
        if (_lastDroppedFiles != null && _lastDroppedFiles.Length > 0)
        {
            var firstImage = _lastDroppedFiles.FirstOrDefault(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp";
            }) ?? _lastDroppedFiles[0];

            annotator.LoadImage(firstImage);
        }
        annotator.Show();
        annotator.Activate();
    }

    private void OnOpenPdfStudioClicked(object sender, RoutedEventArgs e)
    {
        var pdfStudio = _serviceProvider.GetRequiredService<PdfStudioWindow>();
        if (_lastDroppedFiles != null && _lastDroppedFiles.Length > 0)
        {
            var firstPdf = _lastDroppedFiles.FirstOrDefault(f => Path.GetExtension(f).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) ?? _lastDroppedFiles[0];
            pdfStudio.LoadPdf(firstPdf);
        }
        else
        {
            var ofd = new OpenFileDialog
            {
                Title = "Open PDF in Orbit PDF Studio",
                Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                pdfStudio.LoadPdf(ofd.FileName);
            }
        }
        pdfStudio.Show();
        pdfStudio.Activate();
    }

    private void OnOpenCompressorClicked(object sender, RoutedEventArgs e)
    {
        var compressor = _serviceProvider.GetRequiredService<CompressorStudioWindow>();
        if (_lastDroppedFiles != null && _lastDroppedFiles.Length > 0)
        {
            compressor.LoadFile(_lastDroppedFiles[0]);
        }
        compressor.Show();
        compressor.Activate();
    }

    private void OnOpenBatchOperationsClicked(object sender, RoutedEventArgs e)
    {
        var batchWindow = _serviceProvider.GetRequiredService<BatchOperationsWindow>();
        if (_lastDroppedFiles != null && _lastDroppedFiles.Length > 0)
        {
            batchWindow.SetFiles(_lastDroppedFiles);
        }
        batchWindow.Show();
        batchWindow.Activate();
    }

    private void OnOpenSettingsClicked(object sender, RoutedEventArgs e)
    {
        var settingsWindow = _serviceProvider.GetRequiredService<SettingsWindow>();
        settingsWindow.Show();
        settingsWindow.Activate();
    }

    private void OnDroppedOpenAnnotatorClicked(object sender, RoutedEventArgs e) => OnOpenAnnotatorClicked(sender, e);
    private void OnDroppedOpenPdfClicked(object sender, RoutedEventArgs e) => OnOpenPdfStudioClicked(sender, e);
    private void OnDroppedOpenCompressorClicked(object sender, RoutedEventArgs e) => OnOpenCompressorClicked(sender, e);
    private void OnDroppedOpenBatchClicked(object sender, RoutedEventArgs e) => OnOpenBatchOperationsClicked(sender, e);

    private void OnMinimizeToTrayClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    #endregion
}
