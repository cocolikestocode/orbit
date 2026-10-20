namespace Orbit.App.Views;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Orbit.Core.Models;
using Orbit.Core.Services;

public partial class BatchOperationsWindow : Window
{
    private readonly IJobManager _jobManager;
    private readonly IFileIdentifier _fileIdentifier;
    private readonly IOperationRegistry _operationRegistry;

    private readonly ObservableCollection<BatchItemViewModel> _items = new();
    private OperationDefinition? _currentOperation;
    private string? _customOutputFolder;
    private bool _isRunning;
    private bool _isPaused;
    private CancellationTokenSource? _batchCts;
    private readonly DispatcherTimer _timer = new();
    private readonly Stopwatch _stopwatch = new();

    public BatchOperationsWindow(
        IJobManager jobManager,
        IFileIdentifier fileIdentifier,
        IOperationRegistry operationRegistry)
    {
        InitializeComponent();

        _jobManager = jobManager ?? throw new ArgumentNullException(nameof(jobManager));
        _fileIdentifier = fileIdentifier ?? throw new ArgumentNullException(nameof(fileIdentifier));
        _operationRegistry = operationRegistry ?? throw new ArgumentNullException(nameof(operationRegistry));

        FileQueueItemsControl.ItemsSource = _items;

        _jobManager.JobProgressChanged += OnJobProgressChanged;
        _jobManager.JobCompleted += OnJobCompleted;
        _jobManager.JobFailed += OnJobFailed;

        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (s, e) =>
        {
            TxtElapsedTime.Text = $"Elapsed: {_stopwatch.Elapsed:mm\\:ss}";
        };

        PopulateOperations();
    }

    private void PopulateOperations()
    {
        var allOps = _operationRegistry.GetAll()
            .Where(o => o.SupportsBatch)
            .OrderBy(o => o.Priority)
            .ToList();

        if (allOps.Count == 0)
        {
            // Fallback default operations
            allOps = new List<OperationDefinition>
            {
                new()
                {
                    Id = "image-png-to-jpg",
                    Label = "Convert to JPG",
                    Category = OperationCategory.Convert,
                    InputFormats = new[] { ".png" },
                    OutputFormats = new[] { ".jpg" },
                    SupportsBatch = true,
                    EngineId = "image"
                },
                new()
                {
                    Id = "image-png-to-webp",
                    Label = "Convert to WebP",
                    Category = OperationCategory.Convert,
                    InputFormats = new[] { ".png", ".jpg" },
                    OutputFormats = new[] { ".webp" },
                    SupportsBatch = true,
                    EngineId = "image"
                },
                new()
                {
                    Id = "image-jpg-to-png",
                    Label = "Convert to PNG",
                    Category = OperationCategory.Convert,
                    InputFormats = new[] { ".jpg", ".jpeg" },
                    OutputFormats = new[] { ".png" },
                    SupportsBatch = true,
                    EngineId = "image"
                }
            };
        }

        CmbTargetOperation.ItemsSource = allOps;
        CmbTargetOperation.DisplayMemberPath = "Label";
        if (allOps.Count > 0)
        {
            CmbTargetOperation.SelectedIndex = 0;
            _currentOperation = allOps[0];
        }
    }

    public void SetBatch(OperationDefinition operation, FileIdentity[] files)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(files);

        _currentOperation = operation;

        // Select operation in ComboBox
        foreach (var item in CmbTargetOperation.Items)
        {
            if (item is OperationDefinition op && op.Id == operation.Id)
            {
                CmbTargetOperation.SelectedItem = op;
                break;
            }
        }

        _items.Clear();
        foreach (var file in files)
        {
            _items.Add(CreateViewModel(file));
        }

        UpdateQueueUI();
    }

    public void SetFiles(string[] filePaths, OperationDefinition? operation = null)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        if (operation != null)
        {
            _currentOperation = operation;
            foreach (var item in CmbTargetOperation.Items)
            {
                if (item is OperationDefinition op && op.Id == operation.Id)
                {
                    CmbTargetOperation.SelectedItem = op;
                    break;
                }
            }
        }

        _items.Clear();
        var identities = _fileIdentifier.IdentifyMultiple(filePaths);
        foreach (var file in identities)
        {
            _items.Add(CreateViewModel(file));
        }

        UpdateQueueUI();
    }

    public void AddFiles(string[] filePaths)
    {
        if (filePaths == null || filePaths.Length == 0) return;

        var identities = _fileIdentifier.IdentifyMultiple(filePaths);
        foreach (var file in identities)
        {
            if (_items.All(i => !string.Equals(i.FilePath, file.FilePath, StringComparison.OrdinalIgnoreCase)))
            {
                _items.Add(CreateViewModel(file));
            }
        }

        UpdateQueueUI();
    }

    private BatchItemViewModel CreateViewModel(FileIdentity file)
    {
        string dimensions = "-";
        if (file.Family == FileFamily.Image && File.Exists(file.FilePath))
        {
            try
            {
                // Quick read dimensions
                using var stream = File.OpenRead(file.FilePath);
                var frame = System.Windows.Media.Imaging.BitmapFrame.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, System.Windows.Media.Imaging.BitmapCacheOption.None);
                dimensions = $"{frame.PixelWidth} × {frame.PixelHeight}";
            }
            catch
            {
                dimensions = "Image";
            }
        }
        else
        {
            dimensions = file.Family.ToString();
        }

        string sizeText = (file.FileSize / (1024.0 * 1024.0)) >= 1.0
            ? $"{(file.FileSize / (1024.0 * 1024.0)):0.1} MB"
            : $"{(file.FileSize / 1024.0):0} KB";

        string glyph = file.Family switch
        {
            FileFamily.Image => "🖼",
            FileFamily.Document => "📄",
            FileFamily.Archive => "📦",
            FileFamily.Audio => "🎵",
            FileFamily.Video => "🎬",
            FileFamily.Code => "💻",
            _ => "📄"
        };

        return new BatchItemViewModel
        {
            FilePath = file.FilePath,
            FileName = file.FileName,
            Dimensions = dimensions,
            FileSizeText = sizeText,
            Glyph = glyph,
            Progress = 0,
            StatusText = "Queued",
            Identity = file
        };
    }

    private void UpdateQueueUI()
    {
        int count = _items.Count;
        TxtQueueCountBadge.Text = $"{count} file{(count == 1 ? "" : "s")}";

        if (count == 0)
        {
            EmptyQueueBorder.Visibility = Visibility.Visible;
            FileQueueScrollViewer.Visibility = Visibility.Collapsed;
            TxtBatchSubheader.Text = "No files queued • Select an operation and add files to begin";
            TxtOverallProgress.Text = "Overall Progress: Idle";
            TxtCompletedCount.Text = " (0 files)";
            OverallProgressBar.Value = 0;
        }
        else
        {
            EmptyQueueBorder.Visibility = Visibility.Collapsed;
            FileQueueScrollViewer.Visibility = Visibility.Visible;
            var opName = _currentOperation?.Label ?? "Convert to JPG";
            TxtBatchSubheader.Text = $"{count} files queued • Target: {opName}";
            UpdateOverallProgress();
        }
    }

    private void UpdateOverallProgress()
    {
        if (_items.Count == 0) return;

        int doneCount = _items.Count(i => i.IsCompleted);
        double avgProgress = _items.Average(i => i.Progress);

        OverallProgressBar.Value = avgProgress;
        TxtCompletedCount.Text = $" ({doneCount} of {_items.Count} files completed)";

        if (doneCount == _items.Count && _items.Count > 0)
        {
            TxtOverallProgress.Text = "Overall Progress: Complete ✓";
            BtnStartBatch.Content = "✓ Done";
            BtnOpenOutputFolder.Visibility = Visibility.Visible;
        }
        else if (_isRunning)
        {
            TxtOverallProgress.Text = _isPaused 
                ? "Overall Progress: Paused ⏸" 
                : $"Overall Progress: Processing ({avgProgress:0}%)";
        }
    }

    #region Job Manager Event Handlers

    private void OnJobProgressChanged(object? sender, JobInfo job)
    {
        Dispatcher.Invoke(() =>
        {
            var item = _items.FirstOrDefault(i => i.JobId == job.JobId || 
                (job.Request.InputFiles.Length > 0 && job.Request.InputFiles.Any(f => f.FilePath == i.FilePath)));

            if (item != null && job.Progress != null)
            {
                item.Progress = job.Progress.PercentComplete;
                item.StatusText = $"Converting... {job.Progress.PercentComplete:0}%";
                item.SetStatus(BatchItemStatus.Processing);
                UpdateOverallProgress();
            }
        });
    }

    private void OnJobCompleted(object? sender, JobInfo job)
    {
        Dispatcher.Invoke(() =>
        {
            var matchingItems = _items.Where(i => i.JobId == job.JobId || 
                (job.Request.InputFiles.Length > 0 && job.Request.InputFiles.Any(f => f.FilePath == i.FilePath))).ToList();

            foreach (var item in matchingItems)
            {
                item.Progress = 100;
                item.StatusText = "Done";
                item.IsCompleted = true;
                item.SetStatus(BatchItemStatus.Done);

                if (job.Result?.OutputFiles != null && job.Result.OutputFiles.Length > 0)
                {
                    item.OutputPath = job.Result.OutputFiles[0];
                }

                // If keep original is false, clean up original if safe
                if (ChkKeepOriginal.IsChecked == false && !string.IsNullOrEmpty(item.OutputPath) 
                    && !string.Equals(item.FilePath, item.OutputPath, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(item.OutputPath))
                {
                    try
                    {
                        File.Delete(item.FilePath);
                    }
                    catch { /* ignore cleanup fail */ }
                }
            }

            UpdateOverallProgress();
        });
    }

    private void OnJobFailed(object? sender, JobInfo job)
    {
        Dispatcher.Invoke(() =>
        {
            var item = _items.FirstOrDefault(i => i.JobId == job.JobId || 
                (job.Request.InputFiles.Length > 0 && job.Request.InputFiles.Any(f => f.FilePath == i.FilePath)));

            if (item != null)
            {
                item.StatusText = "Failed";
                item.IsFailed = true;
                item.ErrorMessage = job.Result?.ErrorMessage ?? "Operation failed";
                item.SetStatus(BatchItemStatus.Failed);
                UpdateOverallProgress();
            }
        });
    }

    #endregion

    #region Action Buttons & Events

    private async void OnStartBatchClicked(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0)
        {
            MessageBox.Show("Please add files to the batch queue first.", "Batch Operations", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_isRunning)
        {
            // Toggle Pause / Resume
            if (_isPaused)
            {
                _isPaused = false;
                _stopwatch.Start();
                BtnStartBatch.Content = "⏸ Pause";
                TxtOverallProgress.Text = "Overall Progress: Resumed";
            }
            else
            {
                _isPaused = true;
                _stopwatch.Stop();
                BtnStartBatch.Content = "▶ Resume";
                TxtOverallProgress.Text = "Overall Progress: Paused";
            }
            return;
        }

        if (_items.All(i => i.IsCompleted))
        {
            // Reset to run again
            foreach (var item in _items)
            {
                item.Progress = 0;
                item.StatusText = "Queued";
                item.IsCompleted = false;
                item.IsFailed = false;
                item.SetStatus(BatchItemStatus.Queued);
            }
        }

        // Start Execution
        _isRunning = true;
        _isPaused = false;
        _batchCts = new CancellationTokenSource();
        _stopwatch.Restart();
        _timer.Start();

        BtnStartBatch.Content = "⏸ Pause";
        BtnOpenOutputFolder.Visibility = Visibility.Collapsed;

        var op = _currentOperation ?? (OperationDefinition)CmbTargetOperation.SelectedItem;
        string? targetFormat = op.OutputFormats?.FirstOrDefault() ?? "jpg";
        string? destDir = CmbDestination.SelectedIndex == 1 ? _customOutputFolder : null;

        var queuedItems = _items.Where(i => !i.IsCompleted).ToList();

        foreach (var item in queuedItems)
        {
            if (_batchCts.IsCancellationRequested) break;

            while (_isPaused && !_batchCts.IsCancellationRequested)
            {
                await Task.Delay(200);
            }

            if (_batchCts.IsCancellationRequested) break;

            item.SetStatus(BatchItemStatus.Processing);
            item.StatusText = "Starting...";

            var req = new OperationRequest
            {
                OperationId = op.Id,
                InputFiles = new[] { item.Identity },
                OutputFormat = targetFormat,
                OutputDirectory = destDir
            };

            try
            {
                var jobId = await _jobManager.EnqueueAsync(req);
                item.JobId = jobId;
            }
            catch (Exception ex)
            {
                item.StatusText = "Failed";
                item.IsFailed = true;
                item.ErrorMessage = ex.Message;
                item.SetStatus(BatchItemStatus.Failed);
            }

            // Stagger slightly so UI breathes
            await Task.Delay(50);
        }

        // Await all items completing
        while (_items.Any(i => !i.IsCompleted && !i.IsFailed) && !_batchCts.IsCancellationRequested)
        {
            await Task.Delay(300);
        }

        _isRunning = false;
        _stopwatch.Stop();
        _timer.Stop();

        UpdateOverallProgress();
    }

    private void OnCancelBatchClicked(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            _batchCts?.Cancel();
            _isRunning = false;
            _stopwatch.Stop();
            _timer.Stop();
            BtnStartBatch.Content = "▶ Start Batch";
            TxtOverallProgress.Text = "Overall Progress: Cancelled";
        }
        else
        {
            Close();
        }
    }

    private void OnAddFilesClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Add Files to Batch Queue",
            Filter = "All Compatible Files|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.pdf;*.zip;*.7z;*.txt;*.json|All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
        {
            AddFiles(dlg.FileNames);
        }
    }

    private void OnClearQueueClicked(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            MessageBox.Show("Cannot clear queue while a batch is running.", "Orbit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _items.Clear();
        UpdateQueueUI();
    }

    private void OnRemoveItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is BatchItemViewModel item)
        {
            _items.Remove(item);
            UpdateQueueUI();
        }
    }

    private void OnTargetOperationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbTargetOperation.SelectedItem is OperationDefinition op)
        {
            _currentOperation = op;
            TxtBatchSubheader.Text = $"{_items.Count} files queued • Target: {op.Label}";
        }
    }

    private void OnDestinationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BtnBrowseCustomFolder == null) return;

        if (CmbDestination.SelectedIndex == 1)
        {
            BtnBrowseCustomFolder.Visibility = Visibility.Visible;
            if (string.IsNullOrEmpty(_customOutputFolder))
            {
                OnBrowseCustomFolderClicked(sender, e);
            }
        }
        else
        {
            BtnBrowseCustomFolder.Visibility = Visibility.Collapsed;
            _customOutputFolder = null;
        }
    }

    private void OnBrowseCustomFolderClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select Destination Folder"
        };

        if (dlg.ShowDialog() == true)
        {
            _customOutputFolder = dlg.FolderName;
            BtnBrowseCustomFolder.Content = $"📁 {Path.GetFileName(_customOutputFolder)}";
        }
    }

    private void OnOpenOutputFolderClicked(object sender, RoutedEventArgs e)
    {
        string? folderToOpen = _customOutputFolder;
        if (string.IsNullOrEmpty(folderToOpen))
        {
            var firstOut = _items.FirstOrDefault(i => !string.IsNullOrEmpty(i.OutputPath))?.OutputPath;
            if (!string.IsNullOrEmpty(firstOut))
            {
                folderToOpen = Path.GetDirectoryName(firstOut);
            }
            else if (_items.Count > 0)
            {
                folderToOpen = Path.GetDirectoryName(_items[0].FilePath);
            }
        }

        if (!string.IsNullOrEmpty(folderToOpen) && Directory.Exists(folderToOpen))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", folderToOpen) { UseShellExecute = true });
        }
    }

    private void OnFileQueueDragOver(object sender, DragEventArgs e)
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

    private void OnFileQueueDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files != null && files.Length > 0)
        {
            AddFiles(files);
        }
    }

    #endregion
}

public enum BatchItemStatus
{
    Queued,
    Processing,
    Done,
    Failed
}

public class BatchItemViewModel : INotifyPropertyChanged
{
    private double _progress;
    private string _statusText = "Queued";
    private Brush _statusBadgeBackground = new SolidColorBrush(Color.FromRgb(0x2C, 0x2F, 0x3E));
    private Brush _statusBadgeBorder = new SolidColorBrush(Color.FromRgb(0x45, 0x4A, 0x5C));
    private Brush _statusBadgeForeground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string Dimensions { get; init; }
    public required string FileSizeText { get; init; }
    public required string Glyph { get; init; }
    public required FileIdentity Identity { get; init; }

    public string? JobId { get; set; }
    public string? OutputPath { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsFailed { get; set; }

    public double Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public Brush StatusBadgeBackground
    {
        get => _statusBadgeBackground;
        set
        {
            _statusBadgeBackground = value;
            OnPropertyChanged();
        }
    }

    public Brush StatusBadgeBorder
    {
        get => _statusBadgeBorder;
        set
        {
            _statusBadgeBorder = value;
            OnPropertyChanged();
        }
    }

    public Brush StatusBadgeForeground
    {
        get => _statusBadgeForeground;
        set
        {
            _statusBadgeForeground = value;
            OnPropertyChanged();
        }
    }

    public void SetStatus(BatchItemStatus status)
    {
        switch (status)
        {
            case BatchItemStatus.Queued:
                StatusBadgeBackground = new SolidColorBrush(Color.FromRgb(0x2C, 0x2F, 0x3E));
                StatusBadgeBorder = new SolidColorBrush(Color.FromRgb(0x45, 0x4A, 0x5C));
                StatusBadgeForeground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                break;
            case BatchItemStatus.Processing:
                StatusBadgeBackground = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0x7A, 0x1A));
                StatusBadgeBorder = new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x1A));
                StatusBadgeForeground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x33));
                break;
            case BatchItemStatus.Done:
                StatusBadgeBackground = new SolidColorBrush(Color.FromArgb(0x33, 0x10, 0xB9, 0x81));
                StatusBadgeBorder = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                StatusBadgeForeground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
                break;
            case BatchItemStatus.Failed:
                StatusBadgeBackground = new SolidColorBrush(Color.FromArgb(0x33, 0xEF, 0x44, 0x44));
                StatusBadgeBorder = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                StatusBadgeForeground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                break;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
