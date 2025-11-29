# SharpCommander

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Avalonia UI](https://img.shields.io/badge/Avalonia-11.2-purple)](https://avaloniaui.net/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-blue)](https://github.com/aurgo/sharpCommander)

A modern, cross-platform dual-pane file manager built with Avalonia UI. SharpCommander provides an efficient and elegant way to manage your files across Windows, Linux, and macOS.

![SharpCommander Screenshot](docs/screenshot.png)

## ✨ Features

- **🖥️ Cross-Platform**: Runs natively on Windows, Linux, and macOS
- **📁 Dual-Pane Interface**: Work with two directories side by side
- **⚡ Fast File Operations**: Copy, move, and delete with progress tracking
- **🔍 Real-Time Updates**: Automatic file system change detection
- **🎨 Modern Design**: Fluent Design with dark/light theme support
- **⌨️ Keyboard Shortcuts**: F5 (Copy), F6 (Move), Del (Delete), Ctrl+R (Refresh)
- **📊 Detailed Information**: File sizes, types, and modification dates

## 🚀 Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later

### Building from Source

```bash
# Clone the repository
git clone https://github.com/aurgo/sharpCommander.git
cd sharpCommander

# Restore dependencies
dotnet restore SharpCommander.sln

# Build the solution
dotnet build SharpCommander.sln

# Run the application
dotnet run --project src/SharpCommander.Desktop/SharpCommander.Desktop.csproj
```

### Publishing

```bash
# Publish for Windows
dotnet publish src/SharpCommander.Desktop -c Release -r win-x64 --self-contained

# Publish for Linux
dotnet publish src/SharpCommander.Desktop -c Release -r linux-x64 --self-contained

# Publish for macOS
dotnet publish src/SharpCommander.Desktop -c Release -r osx-x64 --self-contained
```

## 🏗️ Architecture

SharpCommander follows a clean architecture pattern with clear separation of concerns:

```
src/
├── SharpCommander.Core/          # Core business logic
│   ├── Models/                   # Domain models
│   └── Interfaces/               # Service interfaces
│
└── SharpCommander.Desktop/       # Avalonia UI application
    ├── Services/                 # Platform-specific implementations
    ├── ViewModels/               # MVVM ViewModels
    ├── Views/                    # XAML Views
    └── Styles/                   # UI Styles and themes
```

### Key Technologies

- **.NET 10**: Latest cross-platform framework
- **Avalonia UI 11.2**: Modern XAML-based UI framework
- **CommunityToolkit.Mvvm**: MVVM utilities and source generators
- **Fluent Design**: Beautiful, consistent UI across platforms

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `F5` | Copy selected files to opposite panel |
| `F6` | Move selected files to opposite panel |
| `Del` | Delete selected files |
| `Ctrl+R` | Refresh both panels |
| `Enter` | Open selected file/folder |
| `Backspace` | Navigate to parent directory |

## 🎨 Themes

SharpCommander supports automatic theme detection based on your system preferences:

- **Light Theme**: Clean, bright interface for daytime use
- **Dark Theme**: Easy on the eyes for nighttime coding sessions
- **System Default**: Automatically matches your OS theme

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📧 Contact

AURGO - [@aurgo](https://github.com/aurgo)

Project Link: [https://github.com/aurgo/sharpCommander](https://github.com/aurgo/sharpCommander)

---

⭐ Star this repository if you find it useful!
