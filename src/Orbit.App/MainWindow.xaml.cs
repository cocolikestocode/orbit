namespace Orbit.App;

using System;
using System.IO;
using System.Linq;
using System.Windows;
using Orbit.Core.Models;
using Orbit.Core.Services;

public partial class MainWindow : Window
{
    private readonly IFileIdentifier _fileIdentifier;
    private readonly IOperationRegistry _operationRegistry;
    private readonly ICapabilityResolver _capabilityResolver;

    public MainWindow(
        IFileIdentifier fileIdentifier,
        IOperationRegistry operationRegistry,
        ICapabilityResolver capabilityResolver)
    {
        InitializeComponent();

        _fileIdentifier = fileIdentifier ?? throw new ArgumentNullException(nameof(fileIdentifier));
        _operationRegistry = operationRegistry ?? throw new ArgumentNullException(nameof(operationRegistry));
        _capabilityResolver = capabilityResolver ?? throw new ArgumentNullException(nameof(capabilityResolver));

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

        var identities = _fileIdentifier.IdentifyMultiple(files);
        var resolved = _capabilityResolver.Resolve(identities);

        var fileSummary = string.Join(", ", identities.Select(f => $"{f.FileName} ({f.Family})"));
        var categories = string.Join(" • ", resolved.AvailableCategories.Select(c => c.ToString()));
        var totalOps = resolved.CategorizedOperations.Values.Sum(l => l.Count);

        TxtDropSummary.Text = $"Identified {files.Length} file(s): {fileSummary}\nAvailable Categories ({resolved.AvailableCategories.Length}): {categories}\nResolved {totalOps} contextual operations (Batch={resolved.IsBatchOperation}, Mixed={resolved.IsMixedSelection})";

        // Update list with resolved operations
        var resolvedList = resolved.CategorizedOperations.Values.SelectMany(x => x).ToList();
        LstResolvedOperations.ItemsSource = resolvedList;
        TxtOperationsCount.Text = $"{resolvedList.Count} contextual operations resolved";
    }

    private void OnMinimizeToTrayClicked(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
        // In full daemon mode, this hides the taskbar window and maintains the tray icon
    }
}
