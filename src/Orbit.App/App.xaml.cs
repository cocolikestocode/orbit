[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Orbit.Core.Tests")]

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

        // Apply initial settings & themes
        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        Orbit.App.Services.ThemeManager.ApplyTheme(settingsService.CurrentSettings);
        _inputMonitor.ActivationModifier = settingsService.CurrentSettings.ActivationModifier;
        _overlayWindow.ApplySettings(settingsService.CurrentSettings);

        settingsService.SettingsChanged += (newSettings) =>
        {
            Dispatcher.Invoke(() =>
            {
                Orbit.App.Services.ThemeManager.ApplyTheme(newSettings);
                _inputMonitor.ActivationModifier = newSettings.ActivationModifier;
                _overlayWindow.ApplySettings(newSettings);
            });
        };

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
        services.AddTransient<PdfStudioWindow>();
        services.AddTransient<CompressorStudioWindow>();
        services.AddTransient<BatchOperationsWindow>();
        services.AddTransient<SettingsWindow>();
    }

    private void OnOrbitTriggered(int screenX, int screenY)
    {
        Dispatcher.Invoke(() =>
        {
            // Query files from active explorer selection or desktop
            var files = ShellSelectionHelper.GetSelectedFiles(screenX, screenY);
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

    public void ExecuteOperationFromAnywhere(OperationDefinition operation, FileIdentity[]? files)
    {
        OnOperationSelected(operation, files ?? Array.Empty<FileIdentity>());
    }

    private async void OnOperationSelected(OperationDefinition operation, FileIdentity[] files)
    {
        if (_serviceProvider == null) return;
        files ??= Array.Empty<FileIdentity>();

        // If no files are pre-selected from Explorer and the operation needs target files, prompt OpenFileDialog
        if (files.Length == 0 &&
            operation.Category != OperationCategory.Annotate &&
            operation.Id != "image-editor" &&
            operation.Id != "doc-edit-pdf")
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Select Target File for {operation.Label}",
                Filter = "All Supported Files (*.*)|*.*|PDF Documents (*.pdf)|*.pdf|Images (*.png;*.jpg;*.webp)|*.png;*.jpg;*.webp|Archives (*.zip;*.7z;*.tar)|*.zip;*.7z;*.tar",
                Multiselect = true
            };
            if (ofd.ShowDialog() == true && ofd.FileNames.Length > 0)
            {
                var identityService = _serviceProvider.GetRequiredService<IFileIdentifier>();
                files = identityService.IdentifyMultiple(ofd.FileNames);
            }
            else
            {
                return; // Cancelled
            }
        }

        bool isImage = files.Length > 0 && files[0].Family == FileFamily.Image;
        bool isPdf = files.Length > 0 && (files[0].Family == FileFamily.Document || files[0].Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase));

        // 1. If an image studio / annotation / crop workspace operation is selected on an image, open ImageAnnotationWindow
        if ((isImage || files.Length == 0) &&
            (operation.Category == OperationCategory.Annotate || 
             operation.Category == OperationCategory.Edit || 
             operation.Id == "image-crop" || 
             operation.Id == "image-editor" ||
             operation.Id == "image-adjust" ||
             operation.Id == "image-add-watermark" ||
             operation.Id.StartsWith("image-annotate") ||
             (operation.RequiresWorkspace && isImage)))
        {
            try
            {
                var annotator = _serviceProvider.GetRequiredService<ImageAnnotationWindow>();
                if (isImage)
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

        // 2. If a PDF document workspace (Edit, Annotate, Reorder) is selected on a PDF, open PdfStudioWindow
        if (isPdf &&
            (operation.RequiresWorkspace ||
             operation.Id == "doc-reorder-pages" ||
             operation.Id == "doc-edit-pdf" ||
             operation.Category == OperationCategory.Annotate))
        {
            try
            {
                var pdfStudio = _serviceProvider.GetRequiredService<PdfStudioWindow>();
                pdfStudio.LoadPdf(files[0].FilePath);
                pdfStudio.Show();
                pdfStudio.Activate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open PDF Studio: {ex.Message}", "Orbit", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }

        // 3. If an Optimize / Compress operation is selected, open CompressorStudioWindow
        if (operation.Category == OperationCategory.Optimize ||
            operation.Id == "image-compress" ||
            operation.Id == "doc-compress-pdf" ||
            operation.Id == "video-compress" ||
            operation.Id == "archive-compress")
        {
            try
            {
                var compressorStudio = _serviceProvider.GetRequiredService<CompressorStudioWindow>();
                if (files.Length > 0)
                {
                    compressorStudio.LoadFile(files[0].FilePath);
                }
                compressorStudio.Show();
                compressorStudio.Activate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open Compression Studio: {ex.Message}", "Orbit", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }

        // 4. Password Protection & Decryption prompts
        string? password = null;
        if (operation.Id == "doc-encrypt-pdf" || operation.Id == "sec-encrypt-file" || operation.Id == "archive-encrypt")
        {
            var (confirmed, pwd) = PasswordInputDialog.Prompt("Protect & Encrypt", $"Set an encryption password for {operation.Label}:");
            if (!confirmed || string.IsNullOrEmpty(pwd)) return;
            password = pwd;
        }
        else if (operation.Id == "doc-decrypt-pdf" || operation.Id == "sec-decrypt-file" || operation.Id == "archive-decrypt")
        {
            var (confirmed, pwd) = PasswordInputDialog.Prompt("Unlock & Decrypt", $"Enter password to decrypt {files.FirstOrDefault()?.FileName ?? "file"}:");
            if (!confirmed || string.IsNullOrEmpty(pwd)) return;
            password = pwd;
        }

        // 5. If multiple files are selected, open BatchOperationsWindow
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

        // 6. Otherwise standard single-file direct job execution
        var jobManager = _serviceProvider.GetRequiredService<IJobManager>();
        var request = new OperationRequest
        {
            OperationId = operation.Id,
            InputFiles = files,
            OutputFormat = operation.OutputFormats?.FirstOrDefault()
        };

        if (!string.IsNullOrEmpty(password))
        {
            request.Parameters["password"] = password;
        }

        try
        {
            string? dispatchedJobId = null;
            EventHandler<JobInfo>? onComplete = null;
            EventHandler<JobInfo>? onFailed = null;

            onComplete = (s, completedJob) =>
            {
                if (dispatchedJobId != null && completedJob.JobId != dispatchedJobId) return;
                jobManager.JobCompleted -= onComplete;
                jobManager.JobFailed -= onFailed;

                Dispatcher.Invoke(() =>
                {
                    var outFiles = completedJob.Result?.OutputFiles ?? Array.Empty<string>();
                    var fileCount = outFiles.Length;
                    var summary = fileCount > 0
                        ? $"Output generated:\n{string.Join("\n", outFiles)}"
                        : "Completed successfully.";

                    var res = MessageBox.Show(
                        $"{operation.Label} completed successfully!\n\n{summary}\n\nDo you want to open the containing folder?",
                        "Orbit — Operation Complete",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (res == MessageBoxResult.Yes && fileCount > 0 && File.Exists(outFiles[0]))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outFiles[0]}\"");
                    }
                });
            };

            onFailed = (s, failedJob) =>
            {
                if (dispatchedJobId != null && failedJob.JobId != dispatchedJobId) return;
                jobManager.JobCompleted -= onComplete;
                jobManager.JobFailed -= onFailed;

                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        $"Operation '{operation.Label}' failed:\n{failedJob.Result?.ErrorMessage ?? "Unknown error"}",
                        "Orbit Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            };

            jobManager.JobCompleted += onComplete;
            jobManager.JobFailed += onFailed;

            dispatchedJobId = await jobManager.EnqueueAsync(request);
            System.Diagnostics.Trace.WriteLine($"[Orbit] Dispatched job {dispatchedJobId} for {operation.Label} on {files.Length} files.");
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
