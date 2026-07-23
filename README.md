<div align="center">
  <img src="Resources/Assets/stride.png" alt="Stride Browser Logo" width="120" />
  <h1>Stride Browser</h1>
  <p><strong>A fast, modern, and privacy-focused web browser built for Windows.</strong></p>

  <!-- Badges -->
  <p>
    <a href="https://github.com/danieldamilola/Stride/releases/latest">
      <img src="https://img.shields.io/github/v/release/danieldamilola/Stride?style=flat-square&color=FF6E30" alt="Latest Release" />
    </a>
    <a href="https://dotnet.microsoft.com/download/dotnet/9.0">
      <img src="https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 9.0" />
    </a>
    <a href="LICENSE">
      <img src="https://img.shields.io/github/license/danieldamilola/Stride?style=flat-square&color=blue" alt="License" />
    </a>
  </p>
</div>

---

## 🚀 Overview

**Stride** is a lightweight, hardware-accelerated Windows desktop web browser. Built on WPF and the Microsoft Edge WebView2 (Chromium) engine, it delivers a lightning-fast native experience while maintaining rendering parity with modern web standards.

We set out to build a browser that rethinks desktop tab management, respects your privacy, and eliminates the bloated aesthetics of modern browsers.

## ✨ Key Features

- 💊 **Favicon Pill Tabs**: A unique "Stride Signature" UI that compresses inactive tabs into clean icons and expands active ones, saving vertical and horizontal screen real estate.
- ⚡ **Command Bar Navigation**: Say goodbye to the permanent, bulky URL bar. Press `Ctrl+L` to invoke a floating, intelligent command bar with instant local history and autocomplete.
- 🛑 **Native Ad & Tracker Blocking**: Built-in network-level blocking capabilities with native support for parsing and applying **uBlock Origin** rules.
- 🧠 **Smart Memory Management**: Intelligent tab hibernation and LRU (Least Recently Used) eviction algorithms to keep memory usage strictly bounded, no matter how many tabs you open.
- 🌙 **Force Dark Mode**: Integrated Dark Reader functionality to seamlessly force dark mode on websites that lack native dark themes.
- 🎯 **Focus Mode**: Distraction-free browsing modes with built-in domain blocklists to keep you on track.
- 🎥 **YouTube Enhancer**: Native integration to enforce default video quality, playback speed, looping, and distraction removal ("Unhook").
- 📦 **OneTab Consolidation**: Built-in tools for sweeping your open tabs into a single, clean list for later reading.

## 📥 Installation (Quick Start)

The easiest way to install Stride is by downloading the pre-compiled installer.

1. Go to the [Releases Page](https://github.com/danieldamilola/Stride/releases).
2. Download the latest `Stride-Setup.exe`.
3. Double-click to run the installer.
   > **Note:** Stride requires the Microsoft .NET 9 Desktop Runtime. If you do not have it installed, our smart installer will automatically download and configure it for you.
4. Launch Stride and enjoy a cleaner web!

## 🛠️ Development & Building from Source

If you want to build Stride yourself, you'll need the following prerequisites:
- **OS**: Windows 10 (1809+) or Windows 11 (x64)
- **SDK**: [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- **Runtime**: [Evergreen WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) *(Usually pre-installed on Windows 11)*

### Build Instructions

1. **Clone the repository:**
   ```bash
   git clone https://github.com/danieldamilola/Stride.git
   cd Stride
   ```
2. **Build the project:**
   ```bash
   dotnet build SpurBrowser.csproj -c Release
   ```
3. **Run the browser:**
   ```bash
   dotnet run --project SpurBrowser.csproj -c Release
   ```
4. **Run Unit Tests:**
   ```bash
   dotnet test SpurBrowser.Tests/SpurBrowser.Tests.csproj
   ```

### Building the Installer
To generate the `.exe` installer yourself, publish the app and use **Inno Setup 6+**:
```bash
dotnet publish SpurBrowser.csproj -c Release -o publish
iscc stride-setup.iss
```

## 🗺️ Roadmap & Future Plans

Stride is constantly evolving. Here is a glimpse into our development roadmap:

- [ ] **Cross-Platform Linux Port (Avalonia):** We are currently exploring bringing the exact Stride experience (and UI parity) to Linux desktop environments via Avalonia UI. 
- [ ] **Extension Marketplace Integration:** Expanding the internal extension engine to support standard Chromium extension manifest formats.
- [ ] **Sync Engine:** Secure, end-to-end encrypted synchronization of history, bookmarks, and settings across multiple devices.
- [ ] **Vertical Tabs Mode:** Optional vertical tab layout for power users with ultrawide monitors.

## 🤝 Contributing

We love open source and welcome contributions from the community! 

Whether you're fixing a bug, suggesting a feature, or writing documentation, please check out our [Contributing Guidelines](CONTRIBUTING.md) to get started.

## 📄 License

Stride is distributed under the MIT License. See [LICENSE](LICENSE) for more information.

Third-party components (Dark Reader, uBlock Origin) are documented in our [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
