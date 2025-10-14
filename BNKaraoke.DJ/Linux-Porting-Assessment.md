# BNKaraoke DJ – Windows-to-Linux Porting Assessment

## Executive summary
- The current BNKaraoke DJ desktop application is a Windows Presentation Foundation (WPF) WinExe that depends on the Windows Desktop runtime, Win32 interop, and Windows-only multimedia tooling.
- Achieving functional and visual parity on Linux requires a wholesale rewrite of the UI layer in a cross-platform framework, plus replacement or abstraction of each Windows-specific dependency (windowing, audio/video enumeration, external tools, configuration paths, deployment).
- The recommended migration target is **Avalonia UI** combined with the cross-platform `LibVLCSharp` runtime and Linux-compatible command-line utilities. Avalonia provides the closest feature parity to WPF (XAML, data binding, templating) while offering an active community and first-party Linux support. Uno Platform is a viable alternative, but its reliance on WebAssembly for Linux desktop scenarios introduces additional complexity and a thinner control surface for WPF-style apps.

---

## Windows-specific surfaces to replace

| Area | Current implementation | Linux considerations |
| ---- | ---------------------- | -------------------- |
| **UI framework** | WPF (`UseWPF`, `Microsoft.WindowsDesktop.App`) with resource dictionaries, behaviors, dependency properties, VisualState-managed gradients, and custom controls (`MarqueePresenter`, `VideoPlayerWindow`, `DJScreen`). | Recreate the visual tree, animations, and bindings in Avalonia (or another XAML-like toolkit). Expect to re-author templates, styles, and pixel-perfect gradients. |
| **Window management** | Heavy use of `user32.dll` via P/Invoke (`SetWindowLong`, focus suppression), Win32 message pump hooks (`HwndSource`), multi-monitor support via `System.Windows.Forms.Screen`, and `Microsoft.Win32.SystemEvents`. | Replace with the chosen toolkit’s window APIs (e.g., Avalonia’s `Window`, `Screens` services). Handle focus rules via the host compositor (Wayland/X11) rather than Win32 flags. |
| **Multimedia stack** | `LibVLCSharp.WPF`, `VideoLAN.LibVLC.Windows`, `NAudio` WASAPI enumerations, Windows-only plugin probing (`win-x64`). | Switch to `LibVLCSharp` with Linux runtimes, adjust plugin probing for `linux-x64`. Replace NAudio usage with LibVLC’s `AudioOutputDeviceEnum` (PulseAudio/ALSA) or a cross-platform audio abstraction. |
| **External tooling** | Bundled `ffmpeg.exe`, `yt-dlp.exe`, ClickOnce/powershell scripts for deployment. | Bundle or depend on Linux-native builds (rename without `.exe`, ensure execute permission). Replace deployment scripts with AppImage/Flatpak/Snap build pipelines or self-contained tarballs. |
| **Dialogs & storage** | `System.Windows.Forms.FolderBrowserDialog`, hard-coded Windows paths (`C:\BNKaraoke\...`, `"\\.DISPLAY1"`), registry usage via .NET config defaults. | Use Avalonia’s cross-platform dialogs and move storage into XDG-compliant locations (`~/.config`, `~/.local/share`). Translate monitor IDs via toolkit services. |
| **Startup assumptions** | Calls `AllocConsole`, relies on ClickOnce update channels. | Replace with cross-platform logging UI; adopt package managers (deb/rpm), AppImage, or manual updates. |

---

## Detailed remediation plan

### 1. Establish the cross-platform UI baseline
1. Create a new Avalonia application project within the solution (targeting .NET 8). Enable `UseCompiledBindings` and shared resource dictionaries for theme parity.
2. Port shared styles and resources (`Themes`, `Styles`, `Assets`). Reimplement custom controls such as `MarqueePresenter` using Avalonia’s `ControlTemplate`, `Animations`, and `Render` overrides to maintain smooth scrolling and crossfade behavior.
3. Prototype the key windows (`DJ Console`, `Singer Screen`, `Video Overlay`) to validate layout fidelity, font rendering, and gradient backgrounds.

> **Why Avalonia?** Avalonia’s XAML dialect and control model are intentionally WPF-like, minimizing mental translation for the existing codebase. Features such as data templates, routed events, and `VisualBrush` analogs support the same presentation patterns already in BNKaraoke DJ.

### 2. Replace Windows-specific windowing and monitor logic
1. Swap Win32 P/Invoke calls with Avalonia window options (`SystemDecorations.None`, `Topmost`, `CanResize`). Use platform-specific services exposed via `IPlatformHandle` sparingly, and gate them behind abstractions.
2. Rebuild the multi-display logic using `Avalonia.Platform.Screens` to detect, assign, and monitor displays. Implement change notifications using Avalonia’s `Screen` events rather than `SystemEvents.DisplaySettingsChanged`.
3. Implement focus suppression and overlay activation by leveraging compositor hints (e.g., disable focusable property on windows, use `Topmost` with `ShowInTaskbar = false`). For behaviors that cannot be replicated exactly (e.g., preventing focus steal across compositors), document the limitations.

### 3. Modernize multimedia and audio handling
1. Update dependencies to `LibVLCSharp` core packages (no WPF suffix) and include Linux runtime assets. Bundle LibVLC native libraries alongside the application or instruct users to install VLC via the system package manager.
2. Replace `NAudio` device enumeration with LibVLC’s cross-platform audio module enumerations (`AudioOutputDeviceEnumerate`). Provide UI affordances for selecting PulseAudio/ALSA devices and persist the selections.
3. Verify video playback surfaces using `LibVLCSharp.Avalonia`. Test hardware acceleration toggles per Linux distro (VAAPI, VDPAU) and expose configuration in settings.

### 4. Normalize external tooling and scripting
1. Obtain Linux builds of `ffmpeg` and `yt-dlp`. Store them in a platform-neutral tools directory (e.g., `Tools/linux-x64/ffmpeg`). Update invocation code to choose the correct binary per OS and set executable permissions at install time.
2. Replace PowerShell deployment scripts with cross-platform tooling (e.g., `dotnet publish` + `appimagetool`, or containerized build scripts using Bash). Document dependencies in `README`.

### 5. Rework configuration, storage, and onboarding
1. Abstract file paths and device identifiers behind a configuration service. Default to XDG paths (`Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)`) and relative device names retrieved from Avalonia/LibVLC enumerations.
2. Replace WinForms dialogs with Avalonia’s `OpenFileDialog`, `OpenFolderDialog`, and custom pickers.
3. Audit fonts and image assets—bundle any that are not guaranteed to be present on Linux (e.g., Segoe UI). Consider open-source alternatives and update design assets accordingly.

### 6. Testing and distribution
1. Set up CI pipelines targeting Windows and Linux (GitHub Actions or Azure Pipelines) to run build checks, UI smoke tests, and integration tests for audio/video playback where possible.
2. Package releases as AppImage (universal), `.deb`/`.rpm` (distro-specific), and optionally Snap/Flatpak for store distribution. Provide documentation for manual installation.

---

## Framework comparison: Avalonia vs. Uno Platform

| Criteria | Avalonia UI | Uno Platform |
| -------- | ----------- | ------------ |
| **Linux desktop target** | Native Skia renderer running on X11/Wayland. First-class support with official packages and tooling (`dotnet new avalonia.app`). | Primarily targets WinUI APIs; Linux desktop relies on the WebAssembly (WASM) head or experimental Skia (GTK) backend. WASM adds browser sandbox constraints; GTK backend is improving but trails Windows/UWP parity. |
| **API familiarity for WPF developers** | Highly WPF-inspired (XAML, bindings, dependency properties, routed events). Minimal conceptual shift for existing BNKaraoke DJ code. | Mimics WinUI/UWP, which diverges from WPF in control templates, behaviors, and dependency property patterns. Additional learning curve for WPF-specific constructs. |
| **Control ecosystem** | Growing marketplace and community libraries focused on desktop scenarios (DataGrid, docking, MVVM integrations). | Strong integration with WinUI/Fluent controls; some advanced desktop controls require additional work or third-party packages. |
| **Tooling & community size** | Smaller than mainstream .NET, but active OSS contributors, JetBrains Rider/Visual Studio integration, and responsive maintainers. Rich documentation and sample gallery. | Backed by nventive, good documentation, but community contributions are narrower; many Linux scenarios rely on experimental branches or preview packages. |
| **Performance considerations** | Skia backend offers GPU acceleration and solid frame rates for complex visuals. Native window integration simplifies video overlay and full-screen experiences. | WASM head incurs performance penalties and complicates native video playback. GTK head is improving but may require additional effort for smooth video rendering. |
| **Licensing & support** | Open-source MIT, commercial support available. | Open-source (Apache 2.0) with enterprise support tiers. |

### Recommendation rationale
- **Visual fidelity:** Avalonia’s styling, templating, and animation systems align with WPF expectations, making it easier to replicate the gradients, marquee animations, and overlay behaviors already implemented.
- **Multimedia integration:** Avalonia’s native windowing and Skia renderer simplify embedding `LibVLCSharp` surfaces without browser intermediation. Uno’s WASM path complicates full-screen video and multi-monitor playback.
- **Developer experience:** Although both communities are smaller than mainstream .NET/TypeScript ecosystems, Avalonia’s emphasis on desktop and the breadth of WPF migration resources reduce risk. Uno shines for WinUI/UWP parity and multi-platform (mobile/web) reach; however, BNKaraoke DJ’s priorities are desktop-specific with demanding video performance.
- **Long-term support:** Avalonia has active sponsors and is being adopted in production by companies shipping desktop apps across Windows, macOS, and Linux. The project’s roadmap includes .NET 9 readiness, compositing improvements, and tooling enhancements that directly benefit this migration.

**Therefore, Avalonia UI is the recommended target** for the Linux port, with Uno considered a secondary option if future requirements demand a shared WinUI codebase across devices.

---

## Suggested next steps
1. **Spike** an Avalonia prototype that hosts LibVLC video playback, marquee animations, and multi-monitor window placement to validate the approach.
2. **Audit** all services and view models for Windows-only APIs; create abstraction interfaces and platform-specific implementations where necessary.
3. **Plan** the deployment pipeline (AppImage/Snap) and gather packaging prerequisites early to avoid surprises late in the port.
4. **Document** any user experience differences stemming from Linux compositor rules (focus behavior, window decorations) and incorporate them into release notes.

With these steps, BNKaraoke DJ can transition from a Windows-only WPF application to a Linux-capable desktop experience while preserving the rich visuals and real-time performance that the current DJ workflow depends on.
