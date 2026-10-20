namespace Orbit.App;

using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
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

    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        // Execution Engines
        services.AddSingleton<IProcessingEngine, ImageProcessingEngine>();
        services.AddSingleton<IProcessingEngine, TextDataProcessingEngine>();
        services.AddSingleton<IJobManager, JobManager>();

        // Native Monitors & Windows
        services.AddSingleton<GlobalInputMonitor>();
        services.AddSingleton<OverlayWindow>();
        services.AddSingleton<MainWindow>();
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
            // If overlay is active, commit the hovered action
            _overlayWindow?.UpdateCursorPosition(screenX, screenY);
        });
    }

    private async void OnOperationSelected(OperationDefinition operation, FileIdentity[] files)
    {
        if (_serviceProvider == null) return;

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
        _inputMonitor?.Dispose();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
