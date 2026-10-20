# Orbit ◎
> **Instant context-aware radial action canvas for Windows files**

Orbit reimagines everyday file manipulation on Windows. Instead of opening heavy desktop applications, uploading sensitive files to ad-supported web converters, or navigating deeply nested context menus, Orbit turns simple file movement into an ambient power tool.

---

## ✦ The Core Interaction

1. **Pick & Drag**: Click and start dragging any file (or multi-file selection) from Windows Explorer, your desktop, or any application.
2. **Hold Shift (Gesture Summon)**: Hold down the `Shift` key while dragging.
3. **Orbit Appears**: A sleek, dark radial canvas blooms immediately under your cursor, populated with contextually resolved actions tailored to the exact file types you are holding.
4. **Flick & Release**: Move your cursor outward across the category sector and action disc, then release. Orbit executes the command instantly in the background without stealing window focus.

```
                  [ Convert ]
            . - ~ ~ ~ ~ ~ - .
        . '       Edit        ' .
      /                             \
[ Extract ]        ◎ Cursor       [ Optimize ]
      \         (File Held)         /
        . '     Annotate      ' .
            ' - _ _ _ _ _ - '
                  [ Secure ]
```

---

## 🖼️ User Interface & Interaction Mockup

```
+-----------------------------------------------------------------------+
|  ◎ Orbit Radial Canvas (Active Gesture Preview)                       |
|                                                                       |
|                          [ Convert to WebP ]                          |
|                                   │                                   |
|             [ Edit ]        ─── TOP ───       [ Optimize ]            |
|                │          /             \          │                  |
|     [ Crop / Resize ] ───(  photo.png    )─── [ Lossless 65% ]        |
|                │          \  (2.4 MB)   /          │                  |
|           [ Annotate ]      ─── BOTTOM ──     [ Strip EXIF ]          |
|                                   │                                   |
|                          [ Combine to PDF ]                           |
+-----------------------------------------------------------------------+
```

*(Mockup asset reference: `docs/assets/orbit-radial-mockup.png`)*

---

## 🛠️ Tech Stack Summary

- **Platform Runtime**: .NET 8 (`net8.0-windows`), C# 12
- **Shell Presentation**: Windows Presentation Foundation (WPF) with hardware-accelerated composition
- **Dependency Injection**: `Microsoft.Extensions.DependencyInjection`
- **Image Processing Engine**: `SixLabors.ImageSharp`
- **Compression & Archive Engine**: `SharpCompress` (ZIP, 7z, TAR, GZ, RAR)
- **Unit & Integration Testing**: `xUnit`, `FluentAssertions`, `Microsoft.NET.Test.Sdk`

---

## 🚀 Getting Started

### Prerequisites
- Windows 10/11 (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Restore & Build

Clone or navigate to the repository directory:

```powershell
# Restore NuGet dependencies
dotnet restore Orbit.sln

# Build the complete solution
dotnet build Orbit.sln -c Release

# Run automated tests
dotnet test Orbit.sln
```

### Launching the Application

```powershell
# Run the Orbit application
dotnet run --project src/Orbit.App/Orbit.App.csproj
```

---

## 📂 Project Structure Overview

```
d:\orbit\
├── Orbit.sln                       # Solution entrypoint binding all projects
├── .gitignore                      # Standard .NET / VS workspace ignores
├── README.md                       # Architecture & developer guide
├── src\
│   ├── Orbit.Core\                 # Core domain models, contracts, and services
│   │   ├── Orbit.Core.csproj
│   │   ├── Models\
│   │   │   ├── FileIdentity.cs     # File family enumeration & file metadata record
│   │   │   ├── OperationDefinition.cs # Operation category, capability & format specifications
│   │   │   ├── OperationRequest.cs # Request, progress, and execution result models
│   │   │   └── OrbitGesture.cs     # Gesture state machine & cursor interaction records
│   │   └── Services\
│   │       ├── IFileIdentifier.cs  # File family & MIME detection contract
│   │       ├── IOperationRegistry.cs # Thread-safe operation registry contract
│   │       ├── ICapabilityResolver.cs # Multi-file & batch capability resolution
│   │       ├── IProcessingEngine.cs # Engine capability & async job execution
│   │       ├── IJobManager.cs      # Background execution queue contract
│   │       └── Impl\
│   │           ├── FileIdentifier.cs # Magic-byte and 22-family extension resolver
│   │           ├── OperationRegistry.cs # ConcurrentDictionary operation registry
│   │           └── CapabilityResolver.cs # Contextual category & operation resolver
│   │
│   ├── Orbit.Engines\              # Specialized operation registries & engine wrappers
│   │   ├── Orbit.Engines.csproj
│   │   └── Registration\
│   │       ├── ImageOperations.cs  # Convert, edit, annotate, optimize, analyze, organize
│   │       ├── DocumentOperations.cs # PDF, Word, Markdown, RTF operations
│   │       ├── ArchiveOperations.cs # ZIP, 7z, TAR, GZ archive manipulation
│   │       ├── MediaOperations.cs   # Audio and video transcode, trim, and extraction
│   │       ├── TextDataOperations.cs # JSON, CSV, XML, YAML, hashing, diffing
│   │       └── AllOperations.cs    # Master aggregator for all operation providers
│   │
│   └── Orbit.App\                  # Windows presentation shell & daemon
│       ├── Orbit.App.csproj
│       ├── App.xaml / App.xaml.cs  # DI container bootstrapper & registry initialization
│       └── MainWindow.xaml / .cs   # Interactive test canvas & tray listener shell
│
└── tests\
    └── Orbit.Core.Tests\           # Automated test suite
        ├── Orbit.Core.Tests.csproj
        ├── FileIdentifierTests.cs   # 25+ extension family & magic byte tests
        ├── OperationRegistryTests.cs # Thread-safety, case-insensitivity & query tests
        └── CapabilityResolverTests.cs # Single file, batch, and mixed selection tests
```

---

## 📜 License

This project is licensed under the [MIT License](LICENSE) — see the LICENSE file for details.
