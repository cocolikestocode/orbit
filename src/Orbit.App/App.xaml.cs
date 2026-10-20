namespace Orbit.App;

using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Orbit.App.Interop;
using Orbit.App.Views;
using Orbit.Core.Models;
using Orbit.Core.Native;
using Orbit.Core.Services;
using Orbit.Core.Services.Impl;
using Orbit.Engines.Execution;
using Orbit.Engines.Registration;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private GlobalInputMonitor? _inputMonitor;
    private OverlayWindow? _overlayWindow;
    private TrayIconManager? _trayIconManager;

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (sender, args) =>
        {
            System.Diagnostics.Trace.WriteLine($"[Orbit Exception] {args.Exception}");
            MessageBox.Show($"Orbit encountered an issue: {args.Exception.Message}", "Orbit Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);

        _serviceProvider = serviceCollection.BuildServiceProvider();
        Services = _serviceProvider;

        // Populate registry with all registered operations across engines
        var registry = _serviceProvider.GetRequiredService<IOperationRegistry>();
        registry.RegisterRange(AllOperations.GetAll());

        // Initialize Overlay Window
        _overlayWindow = _serviceProvider.GetRequiredService<OverlayWindow>();
        _overlayWindow.OperationSelected += OnOperationSelected;

        // Initialize and Start Global Input Monitor
        _inputMonitor = _serviceProvider.GetRequiredService<GlobalInputMonitor>();
        _inputMonitor.OrbitTriggered += OnOrbitTriggered;
        _inputMonitor.DragUpdated += OnDragUpdated;
        _inputMonitor.CategoryShortcutPressed += OnCategoryShortcutPressed;
        _inputMonitor.OrbitDismissed += OnOrbitDismissed;
        _inputMonitor.OrbitCancelled += OnOrbitCancelled;
        _inputMonitor.OrbitCommitted += OnOrbitCommitted;

        try
        {
            _inputMonitor.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Orbit] Could not start low-level hooks: {ex.Message}");
        }

        // Initialize Tray Icon
        _trayIconManager = _serviceProvider.GetRequiredService<TrayIconManager>();

        // Show main control center window
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core Services
        services.AddSingleton<IFileIdentifier, FileIdentifier>();
        services.AddSingleton<IOperationRegistry, OperationRegistry>();
        services.AddSingleton<ICapabilityResolver, CapabilityResolver>();
        services.AddSingleton<ISettingsService, SettingsService>();

        // Execution Engines
        services.AddSingleton<IProcessingEngine, ImageProcessingEngine>();
        services.AddSingleton<IProcessingEngine, TextDataProcessingEngine>();
        services.AddSingleton<IProcessingEngine, ArchiveProcessingEngine>();
        services.AddSingleton<IProcessingEngine, PdfProcessingEngine>();
        services.AddSingleton<IProcessingEngine, MediaProcessingEngine>();
        services.AddSingleton<IJobManager, JobManager>();

        // System Tray Integration
        services.AddSingleton<TrayIconManager>();

        // Native Monitors & Windows
        services.AddSingleton<GlobalInputMonitor>();
        services.AddSingleton<OverlayWindow>();
        services.AddSingleton<MainWindow>();

        // Dedicated UI Workspaces
        services.AddTransient<ImageAnnotationWindow>();
        services.AddTransient<BatchOperationsWindow>();
        services.AddTransient<SettingsWindow>();
    }

    private void OnOrbitTriggered(int screenX, int screenY)
    {
        Dispatcher.Invoke(() =>
        {
            // Query files from active explorer selection
            var files = ShellSelectionHelper.GetSelectedFilesFromForegroundExplorer();
            _overlayWindow?.ShowAt(screenX, screenY, files);
        });
    }

    private void OnDragUpdated(int screenX, int screenY)
    {
        Dispatcher.Invoke(() =>
        {
            _overlayWindow?.UpdateCursorPosition(screenX, screenY);
        });
    }

    private void OnCategoryShortcutPressed(char key)
    {
        Dispatcher.Invoke(() =>
        {
            _overlayWindow?.ForwardCategoryShortcut(key);
        });
    }

    private void OnOrbitDismissed()
    {
        Dispatcher.Invoke(() =>
        {
            _overlayWindow?.Dismiss();
        });
    }

    private void OnOrbitCancelled()
    {
        Dispatcher.Invoke(() =>
        {
            _overlayWindow?.Dismiss();
        });
    }

    private void OnOrbitCommitted(int screenX, int screenY)
    {
        Dispatcher.Invoke(() =>
        {
            if (_overlayWindow != null && _overlayWindow.IsVisible)
            {
                _overlayWindow.CommitAt(screenX, screenY);
            }
        });
    }

    private async void OnOperationSelected(OperationDefinition operation, FileIdentity[] files)
    {
        if (_serviceProvider == null) return;
        files ??= Array.Empty<FileIdentity>();

        bool isImageWorkspace = 
            operation.Category == OperationCategory.Annotate || 
            operation.Category == OperationCategory.Edit || 
            operation.Id == "image-crop" || 
            operation.Id == "image-editor" ||
            operation.Id == "image-adjust" ||
            operation.Id == "image-add-watermark" ||
            operation.Id.StartsWith("image-annotate") ||
            (operation.RequiresWorkspace && (files.Length == 0 || files[0].Family == FileFamily.Image));

        // 1. If an image studio / annotation / crop workspace operation is selected, open ImageAnnotationWindow
        if (isImageWorkspace)
        {
            try
            {
                var annotator = _serviceProvider.GetRequiredService<ImageAnnotationWindow>();
                if (files.Length > 0 && files[0].Family == FileFamily.Image)
                {
                    annotator.LoadImage(files[0].FilePath);
                }

                if (operation.Id == "image-editor" || operation.Category == OperationCategory.Edit)
                {
                    annotator.SetMode(ImageAnnotationWindow.StudioMode.Adjust);
                }
                else
                {
                    annotator.SetMode(ImageAnnotationWindow.StudioMode.Markup, ImageAnnotationWindow.AnnotationTool.Pen);
                }

                annotator.Show();
                annotator.Activate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open Image Studio: {ex.Message}\n\n{ex.StackTrace}", "Orbit", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }

        // 2. If multiple files are selected, open BatchOperationsWindow
        if (files.Length > 1)
        {
            try
            {
                var batchWindow = _serviceProvider.GetRequiredService<BatchOperationsWindow>();
                batchWindow.SetBatch(operation, files);
                batchWindow.Show();
                batchWindow.Activate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open Batch Operations: {ex.Message}\n\n{ex.StackTrace}", "Orbit", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }

        // 3. Otherwise standard single-file direct job execution
        var jobManager = _serviceProvider.GetRequiredService<IJobManager>();
        var request = new OperationRequest
        {
            OperationId = operation.Id,
            InputFiles = files,
            OutputFormat = operation.OutputFormats?.FirstOrDefault()
        };

        try
        {
            var jobId = await jobManager.EnqueueAsync(request);
            System.Diagnostics.Trace.WriteLine($"[Orbit] Dispatched job {jobId} for {operation.Label} on {files.Length} files.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Operation failed to start: {ex.Message}", "Orbit Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconManager?.Dispose();
        _inputMonitor?.Dispose();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
