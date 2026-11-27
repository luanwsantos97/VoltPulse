# Changelog

All notable changes to **VoltPulse** are documented in this file.
Versioning follows clean sequential releases (`v1.0.0`, `v1.0.1`, `v1.0.2`, ...).

## [1.0.5] — 2026-11-27

### Added
- **Automatic Monthly Billing Reset & Month-over-Month Reports (`EnergyStorage` & Energy View)**:
  - The current-month energy and cost counters automatically reset to zero on the 1st of every calendar month while permanently archiving completed months into `monthly_reports.json` (preserving each closed month's effective tariff, total kWh, cost, hours active, average/peak watts, and daily average).
  - New **📅 Relatório Mensal & Comparativo (Mês a Mês)** section in **Energia & Custos** with an interactive 12-month bar chart, month-over-month variation badges (`▲`/`▼ %`), automatic reset countdown, and 1-click **Exportar Mensal (CSV)**.
  - Dashboard **MÊS ATUAL** card now displays live comparison vs. the previous month when available.
- **Automatic "What's New" Release Notes Modal (`LastSeenVersion`)**:
  - Automatically displays a clean, localized **"🎉 Novidades da Versão"** summary modal the first time you open VoltPulse after any update so you immediately see what changed.
  - Reopenable anytime from **Ferramentas → 📋 Ver Novidades** or by clicking the version badge in the sidebar/titlebar.

## [1.0.4] — 2026-11-27

### Security Hardening
- **Auto-Updater Integrity & Isolation (`UpdateChecker`)**: enforces strict HTTPS GitHub release URL allowlisting, downloads updates into an isolated random temporary directory, and verifies published **`SHA256SUMS.txt`** hashes before executing the installer.
- **Authenticode Signature Verification (`DependencyManager` & `VoltPulse.iss`)**: verifies valid Win32 Authenticode digital signatures (`WinVerifyTrust` / `Microsoft` publisher check for WebView2) and uses isolated random temp folders before launching dependency installers.
- **DLL & PATH Hijacking Protection**: added `[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]` across all System32 Win32/ADL P/Invoke declarations (`atiadlxx.dll`, `user32.dll`, `dwmapi.dll`, `gdi32.dll`, `kernel32.dll`, `advapi32.dll`, `wintrust.dll`) and switched `powercfg.exe`, `powershell.exe`, `schtasks.exe`, `taskkill.exe`, and `explorer.exe` to explicit System32/Windows absolute paths.
- **Local HTTP API Hardening (`LocalApiServer`)**: removed wildcard `Access-Control-Allow-Origin: *`, added strict `Host` header validation (`127.0.0.1`/`localhost` anti-DNS-rebinding), restricted HTTP methods to `GET`/`HEAD`, added `X-Content-Type-Options: nosniff` + `Cache-Control: no-store`, and stripped local user directory paths (`DataDirectory`) from the API payload.
- **Elevated WebView2 Lockdown & CSP (`MainForm`, `ui/index.html`)**: blocked all external navigations (`NavigationStarting` restricted to `https://voltpulse.local/`) and popups (`NewWindowRequested`), disabled host objects/autofill, restricted `openUrl` IPC to an HTTPS trusted-host allowlist, and added a strict `Content-Security-Policy` (`script-src 'self'`).
- **Least-Privilege CI/CD & Checksums (`build.ps1`, `.github/workflows/release.yml`)**: automatic `SHA256SUMS.txt` generation and scoped `contents: read` default permissions with `contents: write` isolated solely to tag release publishing.

## [1.0.3] — 2026-11-27

### Fixed & Improved (Gaming & USB Peripheral Shield)
- **Zero USB HID Interference**: disabled `IsControllerEnabled` and `IsPsuEnabled` (`HidSharp`) in `HardwareMonitorService` so VoltPulse never probes or touches USB HID peripherals (wireless mice, keyboards, headsets).
- **Ultra-Low Latency Game Mode Polling**: while a game is running in the background, VoltPulse switches to a lightweight visitor that updates only CPU, GPU, and RAM (`1.5s` interval), skipping heavy LPC/SMBus motherboard SuperIO and SSD S.M.A.R.T. polls to prevent DPC/USB micro-stutters.
- **Safer Eco Profiles (No Auto PCIe ASPM)**: removed automatic `PCIe ASPM = 2` from the `Ultra Silencioso` profile (now manual advanced-only with a wireless peripheral warning) and added `powercfg` change deduplication to stop redundant Windows power-policy broadcasts.
- **Click-Through Mini Widget in Games**: `OverlayForm` now uses permanent `WS_EX_NOACTIVATE` and dynamically enables `WS_EX_TRANSPARENT` during gameplay so it never steals focus or intercepts mouse clicks.
- **1-Click USB & Wireless Mouse Shield (Tools Tab)**: new `🛡️ Blindagem USB & Mouse Sem Fio` tool disables PCIe ASPM, USB Selective Suspend, Windows power-off on USB Root Hubs/Hubs/HID devices, and aggressive Wi-Fi 2.4 GHz roaming scans/MIMO power saving.

## [1.0.2] — 2026-11-26

### Improved
- **Verify Hub UX**: always displays the full diagnostic result screen (efficiency score, active protections, and recommendations) when running a scan or applying optimizations so users clearly see the applied state, while the main circular button displays **`APLICADO`** when optimized (with `← Voltar para Verificar` and click-to-rescan anytime).

## [1.0.1] — 2026-11-26

### Fixed & Improved
- **Anti-Cheat Game Detection (`GameDetector`)**: detects protected games (EA AntiCheat such as *Battlefield 1 / V / 2042*, *EA FC 24/25*, *VALORANT*, etc.) even when `MainWindowHandle` is hidden by kernel anti-cheat, with expanded game catalog and friendly titles.
- **Universal ETW FPS Counter (`FpsMonitor`)**: added automatic `Microsoft-Windows-DxgKrnl` (EventId `184`) fallback without double-counting alongside `DXGI` (level 5, EventIds `42/55/64`) and `D3D9` (level 5) for full DX9/DX11/DX12/Vulkan/OpenGL/Frostbite support.
- **Verify Hub Flow**: automatically returns to the main **Verificar** circular screen with green applied status and score after applying optimizations (or when already 100% optimized), plus a dedicated `← Voltar para Verificar` button and sidebar click return.

## [1.0.0] — 2026-11-25 (Official Public Release)

### Highlights
- **New Slim UI & 1-Click Verify Hub**: clean 5-icon slim sidebar (**Verificar**, **Monitor**, **Otimizar**, **Ferramentas**, **Ajustes**), animated circular **VERIFICAR** button with real hardware/efficiency score (0–100), actionable 1-click optimizations, floating right-side hardware dock, and configurable start screen (`Verificar` or `Monitor`).
- **Real-Time Power & Electricity Cost Tracking**: estimated wall draw (W) with PSU efficiency & base system watts formula, Daily/Weekly/Monthly kWh & cost tracking, Hourly breakdown (00h–23h), 14-day history, Session/Game Cost Meter, Electricity Cost Simulator, and CSV export.
- **Safe Eco Engine Pro (Zero BSOD / Zero MSR Risk)**:
  - Certified 1-click profiles (**Stock**, **Eco Inteligente**, **Ultra Silencioso**) + persistent custom fine-tuning.
  - CPU Speed Shift / CPPC **EPP**, Turbo Boost mode, Passive Cooling & PCIe ASPM via official Windows `powercfg`.
  - Dedicated GPU Power Limit via official **AMD ADL (OverdriveN / Overdrive6)** and **NVIDIA (`nvidia-smi`)** within VBIOS limits (integrated GPUs safely excluded).
  - Smart automations: **Eco outside games**, **Idle / AFK mode**, and **Thermal Guard**.
- **Pro Per-Pixel Alpha Mini Widget (HUD + Safe ETW FPS)**:
  - Zero jagged edges (`UpdateLayeredWindow` 32-bit ARGB with soft floating drop shadow).
  - Right-click to toggle **Horizontal / Vertical** layout, **Auto-hide (peek on hover)**, **Micro Mode**, and 3 transparency levels.
  - Zero-injection, anti-cheat-safe **FPS Counter** via official Windows ETW (`Microsoft-Windows-DXGI` + `D3D9`), active only while gaming with the Mini Widget visible.
- **Complete Hardware Telemetry**:
  - Smart Dedicated vs. Integrated & Hybrid GPU detection (prevents iGPU wattage double-counting with CPU Package).
  - Thermal monitoring with filtered non-temperature sensors, adaptive motherboard/CPU/GPU Voltage cards with ATX ±5% checks, SSD/NVMe/HDD S.M.A.R.T. health & lifespan, Notebook battery wear, Top Processes, Network traffic, and Local JSON API.
- **Native Windows Window Experience**: frameless themed window with native DWM minimize/maximize/restore animations, Aero Snap, zero resize drift, and ultra-low background tray footprint (WebView2 suspend + Windows EcoQoS + 90% fewer SSD writes).
- **Beginner PDF Guide (PT-BR)**: 8-page illustrated guide (`docs/Guia-Didatico-VoltPulse.pdf`) included in the repository and release assets.
