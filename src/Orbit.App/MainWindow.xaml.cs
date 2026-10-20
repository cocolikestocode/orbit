namespace Orbit.App;

using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
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
        var allOps = _operationRegistry.GetAll();
        TxtOperationsCount.Text = $"{allOps.Count} operations active across 5 engines";
        LstResolvedOperations.ItemsSource = allOps.Take(30).ToList();
    }

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

        _lastDroppedFiles = files;
        var identities = _fileIdentifier.IdentifyMultiple(files);
        var resolved = _capabilityResolver.Resolve(identities);

        var fileSummary = string.Join(", ", identities.Select(f => $"{f.FileName} ({f.Family})"));
        var categories = string.Join(" • ", resolved.AvailableCategories.Select(c => c.ToString()));
        var totalOps = resolved.CategorizedOperations.Values.Sum(l => l.Count);

        TxtDropSummary.Text = $"Identified {files.Length} file(s): {fileSummary}\nAvailable Categories ({resolved.AvailableCategories.Length}): {categories}\nResolved {totalOps} contextual operations (Batch={resolved.IsBatchOperation}, Mixed={resolved.IsMixedSelection})";

        // Show workspace quick action buttons
        DropActionButtons.Visibility = Visibility.Visible;
        bool hasImages = identities.Any(f => f.Family == FileFamily.Image);
        BtnDroppedOpenAnnotator.Visibility = hasImages ? Visibility.Visible : Visibility.Collapsed;

        // Update list with resolved operations
        var resolvedList = resolved.CategorizedOperations.Values.SelectMany(x => x).ToList();
        LstResolvedOperations.ItemsSource = resolvedList;
        TxtOperationsCount.Text = $"{resolvedList.Count} contextual operations resolved";
    }

    private void OnOpenAnnotatorClicked(object sender, RoutedEventArgs e)
    {
        var annotator = _serviceProvider.GetRequiredService<ImageAnnotationWindow>();
        if (_lastDroppedFiles != null && _lastDroppedFiles.Length > 0)
        {
            annotator.LoadImage(_lastDroppedFiles[0]);
        }
        annotator.Show();
        annotator.Activate();
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

    private void OnDroppedOpenAnnotatorClicked(object sender, RoutedEventArgs e)
    {
        OnOpenAnnotatorClicked(sender, e);
    }

    private void OnDroppedOpenBatchClicked(object sender, RoutedEventArgs e)
    {
        OnOpenBatchOperationsClicked(sender, e);
    }

    private void OnMinimizeToTrayClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
}
