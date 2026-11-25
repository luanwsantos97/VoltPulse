# Changelog

All notable changes to **VoltPulse** are documented in this file.
Versioning follows clean sequential releases (`v1.0.0`, `v1.0.1`, `v1.0.2`, ...).

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
