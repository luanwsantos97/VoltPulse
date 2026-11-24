(() => {
  const isHost = !!(window.chrome && window.chrome.webview);

  const state = {
    view: "overview",
    lang: "pt-BR",
    payload: null,
    settingsLoaded: false,
    powerSeries: [],   // { time, wall, gpu, cpu }
    tempSeries: [],    // { time, cpu, gpu }
    maxPoints: 60,
    hoverCharts: {}
  };

  const I18N = {
    "pt-BR": {
      nav_overview: "Dashboard",
      nav_energy: "Energia & Custos",
      nav_thermals: "Temperaturas",
      nav_voltages: "Voltagens",
      nav_disks: "Discos & SSD",
      nav_system: "Sistema & Rede",
      nav_settings: "Configurações",
      mini_wall: "Tomada",
      mini_today: "Hoje",
      title_overview: "Dashboard",
      title_energy: "Energia & Custos",
      title_thermals: "Temperaturas",
      title_voltages: "Voltagens (V)",
      title_disks: "Discos & Saúde do SSD (S.M.A.R.T.)",
      title_system: "Sistema, Ventoinhas & Rede",
      title_settings: "Configurações",
      session_title: "Medidor de Sessão / Jogo",
      session_sub: "Meça exatamente quanto uma partida, render ou turno de trabalho gastou de energia",
      sess_dur: "Duração",
      sess_energy: "Energia Gasta",
      sess_cost: "Custo da Sessão",
      sess_peaks: "Picos Térmicos",
      hourly_title: "Consumo por Hora (Hoje 00h–23h)",
      hourly_sub: "Veja em quais horários do dia o PC mais consumiu energia",
      net_title: "Tráfego de Rede em Tempo Real",
      live: "Ao vivo",
      paused: "Pausado",
      start_sess: "▶ Iniciar Sessão",
      stop_sess: "■ Parar Sessão"
    },
    "en-US": {
      nav_overview: "Dashboard",
      nav_energy: "Energy & Costs",
      nav_thermals: "Thermals",
      nav_voltages: "Voltages",
      nav_disks: "Disks & SSD",
      nav_system: "System & Network",
      nav_settings: "Settings",
      mini_wall: "Wall Est.",
      mini_today: "Today",
      title_overview: "Dashboard",
      title_energy: "Energy & Costs",
      title_thermals: "Thermals",
      title_voltages: "Voltages (V)",
      title_disks: "Disks & SSD Health (S.M.A.R.T.)",
      title_system: "System, Fans & Network",
      title_settings: "Settings",
      session_title: "Session / Gaming Meter",
      session_sub: "Measure exact power usage, cost and peak temps during a gaming or work session",
      sess_dur: "Duration",
      sess_energy: "Energy Used",
      sess_cost: "Session Cost",
      sess_peaks: "Thermal Peaks",
      hourly_title: "Hourly Consumption (Today 00h–23h)",
      hourly_sub: "See which hours of the day your PC drew the most power",
      net_title: "Live Network Traffic",
      live: "Live",
      paused: "Paused",
      start_sess: "▶ Start Session",
      stop_sess: "■ Stop Session"
    }
  };

  function tr(key) {
    const dict = I18N[state.lang] || I18N["pt-BR"];
    return dict[key] || I18N["pt-BR"][key] || key;
  }

  function applyLanguage(lang) {
    state.lang = lang === "en-US" ? "en-US" : "pt-BR";
    document.querySelectorAll("[data-i18n]").forEach((el) => {
      el.textContent = tr(el.dataset.i18n);
    });
    $("pageTitle").textContent = tr("title_" + state.view);
    if ($("btnLangToggle")) $("btnLangToggle").textContent = state.lang === "en-US" ? "EN" : "PT";
  }

  const $ = (id) => document.getElementById(id);

  function sendHost(obj) {
    if (isHost) {
      window.chrome.webview.postMessage(obj);
    }
  }

  function fmtNum(v, dec = 1) {
    if (v === null || v === undefined || Number.isNaN(v)) return "--";
    return Number(v).toLocaleString("pt-BR", { minimumFractionDigits: dec, maximumFractionDigits: dec });
  }

  function fmtMoney(v, cur = "R$") {
    return `${cur} ${fmtNum(v, 2)}`;
  }

  function fmtHours(h) {
    const mins = Math.round((h || 0) * 60);
    const hh = Math.floor(mins / 60);
    const mm = mins % 60;
    return hh > 0 ? `${hh}h ${String(mm).padStart(2, "0")}m` : `${mm}m`;
  }

  function tempColor(c, warn = 75, crit = 88) {
    if (c === null || c === undefined) return "var(--text-muted)";
    if (c >= crit) return "var(--red)";
    if (c >= warn) return "var(--amber)";
    if (c <= 45) return "var(--cyan)";
    return "var(--green)";
  }

  function tempStatus(c, warn = 75, crit = 88) {
    if (c >= crit) return { label: "Crítico", cls: "crit" };
    if (c >= warn) return { label: "Quente", cls: "warn" };
    if (c <= 45) return { label: "Frio", cls: "cool" };
    return { label: "Ideal", cls: "ok" };
  }

  function catClass(cat) {
    if (!cat) return "";
    const k = cat.toLowerCase();
    if (k.includes("gpu")) return "gpu";
    if (k.includes("placa")) return "mb";
    if (k.includes("armazen")) return "disk";
    return "";
  }

  function setGauge(fillId, pct, color) {
    const el = $(fillId);
    if (!el) return;
    const clamped = Math.max(0, Math.min(100, pct || 0));
    const arcLen = 226;
    el.style.strokeDashoffset = String(arcLen - (arcLen * clamped) / 100);
    if (color) el.style.stroke = color;
  }

  function showToast(msg) {
    const t = $("toast");
    t.textContent = msg;
    t.classList.remove("hidden");
    clearTimeout(t._timer);
    t._timer = setTimeout(() => t.classList.add("hidden"), 2600);
  }

  // Navigation
  function switchView(view) {
    state.view = view;
    document.querySelectorAll(".nav-item").forEach((b) => {
      b.classList.toggle("active", b.dataset.view === view);
    });
    document.querySelectorAll(".view").forEach((s) => {
      s.classList.toggle("active", s.id === `view-${view}`);
    });
    $("pageTitle").textContent = tr("title_" + view);
    requestAnimationFrame(() => redrawAllCharts());
  }

  document.querySelectorAll(".nav-item").forEach((btn) => {
    btn.addEventListener("click", () => switchView(btn.dataset.view));
  });

  document.querySelectorAll("[data-goto]").forEach((btn) => {
    btn.addEventListener("click", () => switchView(btn.dataset.goto));
  });

  // Top/Sidebar Controls
  $("btnPause").addEventListener("click", () => {
    if (isHost) {
      sendHost({ action: "togglePause" });
    } else if (state.payload) {
      state.payload.Telemetry.Paused = !state.payload.Telemetry.Paused;
      render(state.payload);
    }
  });

  if ($("btnOverlayToggle")) {
    $("btnOverlayToggle").addEventListener("click", () => {
      if (isHost) {
        sendHost({ action: "toggleOverlay" });
      } else {
        showToast("Overlay disponível no app desktop VoltPulse.exe");
      }
    });
  }

  if ($("btnLangToggle")) {
    $("btnLangToggle").addEventListener("click", () => {
      const next = state.lang === "pt-BR" ? "en-US" : "pt-BR";
      applyLanguage(next);
      if ($("setLang")) $("setLang").value = next;
      if (state.payload && state.payload.Settings) {
        state.payload.Settings.Language = next;
        sendHost({ action: "saveSettings", settings: state.payload.Settings });
      }
    });
  }

  if ($("btnToggleSession")) {
    $("btnToggleSession").addEventListener("click", () => {
      const active = state.payload && state.payload.Session && state.payload.Session.Active;
      if (active) {
        sendHost({ action: "stopSession" });
        showToast(state.lang === "en-US" ? "Session stopped." : "Sessão finalizada!");
      } else {
        const name = ($("sessionNameInput") && $("sessionNameInput").value) || (state.lang === "en-US" ? "Session" : "Sessão");
        sendHost({ action: "startSession", name });
        showToast(state.lang === "en-US" ? "Session measurement started." : "Medição de sessão iniciada!");
      }
    });
  }

  $("btnThemeToggle").addEventListener("click", () => {
    const cur = document.documentElement.getAttribute("data-theme") || "dark";
    const next = cur === "dark" ? "light" : "dark";
    applyTheme(next);
    if (state.payload && state.payload.Settings) {
      state.payload.Settings.Theme = next;
      $("setTheme").value = next;
      sendHost({ action: "saveSettings", settings: state.payload.Settings });
    }
    redrawAllCharts();
  });

  function applyTheme(theme) {
    document.documentElement.setAttribute("data-theme", theme === "light" ? "light" : "dark");
    const icon = $("themeIcon");
    if (theme === "light") {
      icon.innerHTML = '<circle cx="12" cy="12" r="5"/><line x1="12" y1="1" x2="12" y2="3"/><line x1="12" y1="21" x2="12" y2="23"/><line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/><line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/><line x1="1" y1="12" x2="3" y2="12"/><line x1="21" y1="12" x2="23" y2="12"/><line x1="4.22" y1="19.78" x2="5.64" y2="18.36"/><line x1="18.36" y1="5.64" x2="19.78" y2="4.22"/>';
    } else {
      icon.innerHTML = '<path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/>';
    }
  }

  // Filters
  $("filterTempCat").addEventListener("change", () => state.payload && renderThermals(state.payload));
  $("filterVoltCat").addEventListener("change", () => state.payload && renderVoltages(state.payload));
  $("searchVoltage").addEventListener("input", () => state.payload && renderVoltages(state.payload));

  // Energy actions
  $("btnExportCsv").addEventListener("click", () => sendHost({ action: "exportCsv" }));
  $("btnOpenFolder").addEventListener("click", () => sendHost({ action: "openDataDir" }));
  $("btnPawnLink").addEventListener("click", () => sendHost({ action: "openUrl", url: "https://pawnio.eu/" }));
  if ($("btnInstallDeps")) {
    $("btnInstallDeps").addEventListener("click", () => sendHost({ action: "installDeps" }));
  }

  $("btnClearHistory").addEventListener("click", () => {
    openModal(
      "Zerar histórico de energia?",
      "Todos os registros diários de consumo acumulados serão apagados permanentemente.",
      () => {
        sendHost({ action: "clearHistory" });
        showToast("Histórico zerado.");
      }
    );
  });

  // Modal
  let modalOnConfirm = null;
  function openModal(title, body, onConfirm) {
    $("modalTitle").textContent = title;
    $("modalBody").textContent = body;
    modalOnConfirm = onConfirm;
    $("confirmModal").classList.remove("hidden");
  }
  $("modalCancel").addEventListener("click", () => $("confirmModal").classList.add("hidden"));
  $("modalConfirm").addEventListener("click", () => {
    $("confirmModal").classList.add("hidden");
    if (modalOnConfirm) modalOnConfirm();
  });

  // Simulator
  function updateSimulator() {
    const hrs = Number($("simHours").value) || 8;
    const watts = Number($("simWatts").value) || 150;
    const tariff = (state.payload && state.payload.Settings && state.payload.Settings.TariffPerKwh) || 0.85;
    const cur = (state.payload && state.payload.Settings && state.payload.Settings.CurrencySymbol) || "R$";

    $("simHoursLabel").textContent = `${hrs} h/dia`;
    $("simWattsLabel").textContent = `${watts} W`;

    const dayKwh = (watts * hrs) / 1000;
    const weekKwh = dayKwh * 7;
    const monthKwh = dayKwh * 30;

    $("simDayCost").textContent = fmtMoney(dayKwh * tariff, cur);
    $("simDayKwh").textContent = `${fmtNum(dayKwh, 2)} kWh`;
    $("simWeekCost").textContent = fmtMoney(weekKwh * tariff, cur);
    $("simWeekKwh").textContent = `${fmtNum(weekKwh, 2)} kWh`;
    $("simMonthCost").textContent = fmtMoney(monthKwh * tariff, cur);
    $("simMonthKwh").textContent = `${fmtNum(monthKwh, 1)} kWh`;
  }

  $("simHours").addEventListener("input", updateSimulator);
  $("simWatts").addEventListener("input", updateSimulator);
  $("simUseCurrent").addEventListener("click", () => {
    if (!state.payload) return;
    $("simWatts").value = Math.round(state.payload.Telemetry.EstimatedWallWatts / 10) * 10 || 100;
    updateSimulator();
  });
  $("simUseTodayAvg").addEventListener("click", () => {
    if (!state.payload) return;
    const avg = state.payload.Summary.TodayAvgWatts || state.payload.Telemetry.EstimatedWallWatts;
    $("simWatts").value = Math.round(avg / 10) * 10 || 100;
    updateSimulator();
  });
  document.querySelectorAll("[data-simw]").forEach((b) => {
    b.addEventListener("click", () => {
      $("simWatts").value = b.dataset.simw;
      updateSimulator();
    });
  });

  // Settings presets & save
  document.querySelectorAll("[data-set-eff]").forEach((b) => {
    b.addEventListener("click", () => { $("setPsuEff").value = b.dataset.setEff; });
  });
  document.querySelectorAll("[data-set-base]").forEach((b) => {
    b.addEventListener("click", () => { $("setBaseWatts").value = b.dataset.setBase; });
  });

  $("btnSaveSettings").addEventListener("click", () => {
    const newSettings = {
      TariffPerKwh: parseFloat($("setTariff").value) || 0.85,
      CurrencySymbol: ($("setCurrency").value || "R$").trim(),
      PsuEfficiencyPercent: parseFloat($("setPsuEff").value) || 85,
      BaseSystemWatts: parseFloat($("setBaseWatts").value) || 30,
      PollIntervalSeconds: parseInt($("setInterval").value, 10) || 2,
      TempWarningC: parseFloat($("setTempWarn").value) || 75,
      TempCriticalC: parseFloat($("setTempCrit").value) || 88,
      MinimizeToTrayOnClose: $("setMinTray").checked,
      StartWithWindows: $("setStartWin").checked,
      Theme: $("setTheme").value === "light" ? "light" : "dark",
      Language: ($("setLang") && $("setLang").value === "en-US") ? "en-US" : "pt-BR",
      EnableAlerts: $("setAlerts") ? $("setAlerts").checked : true,
      MonthlyBudgetAlert: $("setBudget") ? (parseFloat($("setBudget").value) || 0) : 0,
      ShowOverlay: $("setShowOverlay") ? $("setShowOverlay").checked : false
    };
    applyTheme(newSettings.Theme);
    applyLanguage(newSettings.Language);
    if (isHost) {
      sendHost({ action: "saveSettings", settings: newSettings });
    } else if (state.payload) {
      state.payload.Settings = newSettings;
      render(state.payload);
    }
    $("saveHint").textContent = "✓ Salvo!";
    showToast("Configurações salvas e aplicadas.");
    setTimeout(() => { $("saveHint").textContent = ""; }, 2500);
  });

  // RENDER MAIN PAYLOAD
  function render(payload) {
    state.payload = payload;
    const t = payload.Telemetry;
    const s = payload.Summary;
    const cfg = payload.Settings;
    const cur = cfg.CurrencySymbol || "R$";

    if (!state.settingsLoaded) {
      state.settingsLoaded = true;
      applyTheme(cfg.Theme || "dark");
      applyLanguage(cfg.Language || "pt-BR");
      $("setTariff").value = cfg.TariffPerKwh;
      $("setCurrency").value = cur;
      $("setPsuEff").value = cfg.PsuEfficiencyPercent;
      $("setBaseWatts").value = cfg.BaseSystemWatts;
      $("setInterval").value = cfg.PollIntervalSeconds;
      $("setTempWarn").value = cfg.TempWarningC;
      $("setTempCrit").value = cfg.TempCriticalC;
      $("setMinTray").checked = !!cfg.MinimizeToTrayOnClose;
      $("setStartWin").checked = !!cfg.StartWithWindows;
      $("setTheme").value = cfg.Theme || "dark";
      if ($("setLang")) $("setLang").value = cfg.Language || "pt-BR";
      if ($("setAlerts")) $("setAlerts").checked = cfg.EnableAlerts !== false;
      if ($("setBudget")) $("setBudget").value = cfg.MonthlyBudgetAlert || 0;
      if ($("setShowOverlay")) $("setShowOverlay").checked = !!cfg.ShowOverlay;
    }

    if ($("btnOverlayToggle")) {
      $("btnOverlayToggle").classList.toggle("active-toggle", !!cfg.ShowOverlay);
    }
    if ($("setShowOverlay") && document.activeElement !== $("setShowOverlay")) {
      $("setShowOverlay").checked = !!cfg.ShowOverlay;
    }

    if (payload.AppVersion) $("appVersionBadge").textContent = "v" + payload.AppVersion;

    // Header
    $("hwStrip").textContent = `${t.CpuName}  •  ${t.GpuName}  •  ${t.MotherboardName}`;
    $("badgeLastUpdate").textContent = t.TimeLabel || "--:--:--";

    const badgeAdmin = $("badgeAdmin");
    if (t.IsAdmin) {
      badgeAdmin.textContent = "● Modo Admin";
      badgeAdmin.className = "badge badge-ok";
    } else {
      badgeAdmin.textContent = "⚠ Sem Admin";
      badgeAdmin.className = "badge badge-warn";
    }

    // Banner (Admin / PawnIO)
    const banner = $("alertBanner");
    const bannerTxt = $("alertBannerText");
    const bannerAct = $("alertBannerAction");
    if (!t.IsAdmin) {
      banner.classList.remove("hidden");
      bannerTxt.textContent = "Execute em Modo Administrador para liberar todos os sensores de voltagem da placa-mãe e potência/temperatura da CPU.";
      bannerAct.textContent = "Reiniciar como Admin";
      bannerAct.onclick = () => sendHost({ action: "restartAdmin" });
    } else if (!t.PawnIoInstalled && t.CpuWatts === 0 && t.CpuTempC === null) {
      banner.classList.remove("hidden");
      bannerTxt.textContent = "Driver PawnIO não detectado. Instale automaticamente para liberar sensores da CPU, placa-mãe e SSD/NVMe.";
      bannerAct.textContent = "Instalar PawnIO Agora";
      bannerAct.onclick = () => sendHost({ action: "installDeps" });
    } else {
      banner.classList.add("hidden");
    }

    // Sidebar status
    $("liveDot").className = "status-dot " + (t.Paused ? "paused" : "live");
    $("btnPauseText").textContent = t.Paused ? tr("paused") : tr("live");
    $("sideWatts").textContent = `${fmtNum(t.EstimatedWallWatts, 0)} W`;
    const cpuT = t.CpuTempC !== null ? `${fmtNum(t.CpuTempC, 0)}°` : "--°";
    const gpuT = t.GpuTempC !== null ? `${fmtNum(t.GpuTempC, 0)}°` : "--°";
    $("sideTemps").textContent = `${cpuT} / ${gpuT}`;
    $("sideToday").textContent = fmtMoney(s.TodayCost, cur);
    $("navTempBadge").textContent = t.CpuTempC !== null ? `${fmtNum(t.CpuTempC, 0)}°C` : (t.GpuTempC !== null ? `${fmtNum(t.GpuTempC, 0)}°C` : "--°");
    $("navVoltCount").textContent = String((t.Voltages || []).length);
    const diskPill = $("navDiskHealth");
    if (diskPill) {
      diskPill.textContent = t.MinDiskHealthPercent !== null && t.MinDiskHealthPercent !== undefined
        ? `${t.MinDiskHealthPercent}%`
        : `${(t.Disks || []).length}`;
    }

    // KPI Cards
    $("kpiEffChip").textContent = `${fmtNum(cfg.PsuEfficiencyPercent, 0)}% PSU`;
    $("kpiNowWatts").textContent = `${fmtNum(t.EstimatedWallWatts, 1)} W`;
    $("kpiNowSub").textContent = `CPU ${fmtNum(t.CpuWatts, 1)}W • GPU ${fmtNum(t.GpuWatts, 1)}W • Base ${fmtNum(cfg.BaseSystemWatts, 0)}W`;

    $("kpiTodayHours").textContent = fmtHours(s.TodayActiveHours);
    $("kpiTodayCost").textContent = fmtMoney(s.TodayCost, cur);
    $("kpiTodaySub").textContent = `${fmtNum(s.TodayKwh, 3)} kWh • Média ${fmtNum(s.TodayAvgWatts, 0)}W • Pico ${fmtNum(s.TodayPeakWatts, 0)}W`;

    $("kpiWeekDays").textContent = `${s.WeekDaysRecorded}d`;
    $("kpiWeekCost").textContent = fmtMoney(s.WeekCost, cur);
    $("kpiWeekSub").textContent = `${fmtNum(s.WeekKwh, 2)} kWh • Média ${fmtNum(s.WeekAvgDailyKwh, 2)} kWh/dia`;

    $("kpiMonthCost").textContent = fmtMoney(s.MonthCost, cur);
    $("kpiMonthSub").textContent = s.MonthDaysRecorded > 0
      ? `Proj. mês: ${fmtNum(s.ProjectedMonthKwh, 1)} kWh (${fmtMoney(s.ProjectedMonthCost, cur)})`
      : `${fmtNum(s.MonthKwh, 2)} kWh acumulados`;

    // Gauges
    const cTemp = t.CpuTempC;
    $("gCpuTempVal").textContent = cTemp !== null ? `${fmtNum(cTemp, 0)}°C` : "--°C";
    setGauge("gCpuTempFill", cTemp !== null ? (cTemp / 100) * 100 : 0, tempColor(cTemp, cfg.TempWarningC, cfg.TempCriticalC));
    $("gCpuLoad").textContent = `Uso: ${t.CpuLoadPercent !== null ? fmtNum(t.CpuLoadPercent, 0) + "%" : "--%"}`;
    $("gCpuClock").textContent = t.CpuClockMhz !== null ? `${fmtNum(t.CpuClockMhz / 1000, 2)} GHz` : "-- GHz";

    const gTemp = t.GpuTempC;
    $("gGpuTempVal").textContent = gTemp !== null ? `${fmtNum(gTemp, 0)}°C` : "--°C";
    setGauge("gGpuTempFill", gTemp !== null ? (gTemp / 100) * 100 : 0, tempColor(gTemp, cfg.TempWarningC, cfg.TempCriticalC));
    $("gGpuLoad").textContent = `Uso: ${t.GpuLoadPercent !== null ? fmtNum(t.GpuLoadPercent, 0) + "%" : "--%"}`;
    $("gGpuWatts").textContent = `${fmtNum(t.GpuWatts, 0)} W`;

    const wallPct = Math.min(100, (t.EstimatedWallWatts / 500) * 100);
    $("gPowerVal").textContent = `${fmtNum(t.EstimatedWallWatts, 0)} W`;
    setGauge("gPowerFill", wallPct, t.EstimatedWallWatts > 350 ? "var(--amber)" : "var(--cyan)");
    $("gPowerComp").textContent = `Comp: ${fmtNum(t.ComponentsTotalWatts, 0)} W`;
    const costPerHour = (t.EstimatedWallWatts / 1000) * cfg.TariffPerKwh;
    $("gPowerHour").textContent = `${fmtMoney(costPerHour, cur)}/h`;

    const ramPct = t.RamLoadPercent || 0;
    $("gRamVal").textContent = t.RamLoadPercent !== null ? `${fmtNum(ramPct, 0)}%` : "--%";
    setGauge("gRamFill", ramPct, ramPct > 85 ? "var(--rose)" : "var(--purple)");
    $("gRamDetail").textContent = t.RamUsedGb !== null && t.RamTotalGb !== null
      ? `${fmtNum(t.RamUsedGb, 1)} / ${fmtNum(t.RamTotalGb, 0)} GB`
      : "-- / -- GB";
    $("gFanQuick").textContent = t.MaxFanRpm !== null ? `Fan: ${fmtNum(t.MaxFanRpm, 0)} RPM` : "Fan: --";

    // Series buffer (apenas se não pausado)
    if (!t.Paused) {
      state.powerSeries.push({
        time: t.TimeLabel,
        wall: t.EstimatedWallWatts,
        gpu: t.GpuWatts,
        cpu: t.CpuWatts
      });
      if (state.powerSeries.length > state.maxPoints) state.powerSeries.shift();

      state.tempSeries.push({
        time: t.TimeLabel,
        cpu: t.CpuTempC,
        gpu: t.GpuTempC
      });
      if (state.tempSeries.length > state.maxPoints) state.tempSeries.shift();
    }

    // Overview Power Sensors table
    renderOverviewPower(t);
    renderSession(payload);
    renderHistoryTable(s, cfg);
    renderThermals(payload);
    renderVoltages(payload);
    renderDisks(payload);
    renderNetwork(t);
    renderSystem(payload);
    updateSimulator();

    // Diagnostics
    $("diagAdmin").textContent = t.IsAdmin ? "Administrador (OK)" : "Usuário Padrão (Limitado)";
    $("diagPawn").textContent = t.PawnIoInstalled ? "Instalado (OK)" : "Não detectado";
    $("diagDataDir").textContent = payload.DataDirectory || "%LocalAppData%\\PcPowerMonitor";

    redrawAllCharts();
  }

  function renderOverviewPower(t) {
    const tbody = $("tblOverviewPower");
    if (!t.PowerSensors || t.PowerSensors.length === 0) {
      tbody.innerHTML = '<tr><td colspan="4" class="muted">Nenhum sensor de potência reportado.</td></tr>';
      return;
    }
    tbody.innerHTML = t.PowerSensors.map((p) => `
      <tr>
        <td><span class="cat-pill ${catClass(p.HardwareType)}">${esc(p.HardwareType)}</span></td>
        <td>${esc(p.SensorName)}</td>
        <td class="num cyan"><strong>${fmtNum(p.Value, 1)} W</strong></td>
        <td class="num muted">${fmtNum(p.Max, 1)} W</td>
      </tr>
    `).join("");
  }

  function renderHistoryTable(s, cfg) {
    const tbody = $("tblHistory");
    const cur = cfg.CurrencySymbol || "R$";
    if (!s.AllDays || s.AllDays.length === 0) {
      tbody.innerHTML = '<tr><td colspan="6" class="muted">Ainda acumulando o primeiro dia...</td></tr>';
      return;
    }
    tbody.innerHTML = s.AllDays.map((d) => {
      const cost = d.EnergyKwh * cfg.TariffPerKwh;
      return `
        <tr>
          <td><strong>${formatDateBr(d.Date)}</strong></td>
          <td>${fmtHours(d.ActiveSeconds / 3600)}</td>
          <td class="num">${fmtNum(d.AverageWatts, 1)} W</td>
          <td class="num muted">${fmtNum(d.PeakWatts, 1)} W</td>
          <td class="num cyan"><strong>${fmtNum(d.EnergyKwh, 3)} kWh</strong></td>
          <td class="num green"><strong>${fmtMoney(cost, cur)}</strong></td>
        </tr>
      `;
    }).join("");
  }

  function renderThermals(payload) {
    const t = payload.Telemetry;
    const cfg = payload.Settings;
    const cards = [
      { label: "CPU Principal", val: t.CpuTempC, sub: t.CpuName },
      { label: "GPU Core", val: t.GpuTempC, sub: t.GpuName },
      { label: "GPU Hot Spot", val: t.GpuHotspotC, sub: "Ponto mais quente da GPU" },
      { label: "Armazenamento (Máx)", val: t.MaxStorageTempC, sub: (t.StorageNames && t.StorageNames[0]) || "SSD / HD" }
    ];

    $("thermalCards").innerHTML = cards.map((c) => {
      const col = tempColor(c.val, cfg.TempWarningC, cfg.TempCriticalC);
      return `
        <div class="mini-stat-card">
          <div class="mini-stat-label">${esc(c.label)}</div>
          <div class="mini-stat-val" style="color:${col}">${c.val !== null && c.val !== undefined ? fmtNum(c.val, 1) + " °C" : "-- °C"}</div>
          <div class="mini-stat-sub">${esc(c.sub || "")}</div>
        </div>
      `;
    }).join("");

    const filter = $("filterTempCat").value;
    const list = (t.Temperatures || []).filter((x) => filter === "ALL" || x.HardwareType === filter);
    const tbody = $("tblTemps");
    if (list.length === 0) {
      tbody.innerHTML = '<tr><td colspan="8" class="muted">Nenhum sensor de temperatura nesta categoria.</td></tr>';
      return;
    }

    tbody.innerHTML = list.map((x) => {
      const st = tempStatus(x.Value, cfg.TempWarningC, cfg.TempCriticalC);
      const pct = Math.max(4, Math.min(100, x.Value));
      const barCol = tempColor(x.Value, cfg.TempWarningC, cfg.TempCriticalC);
      return `
        <tr>
          <td><span class="cat-pill ${catClass(x.HardwareType)}">${esc(x.HardwareType)}</span></td>
          <td class="muted">${esc(x.HardwareName)}</td>
          <td><strong>${esc(x.SensorName)}</strong></td>
          <td><div class="bar-inline"><div style="width:${pct}%;background:${barCol}"></div></div></td>
          <td class="num" style="color:${barCol}"><strong>${fmtNum(x.Value, 1)} °C</strong></td>
          <td class="num muted">${fmtNum(x.Min, 1)} °C</td>
          <td class="num muted">${fmtNum(x.Max, 1)} °C</td>
          <td><span class="status-tag ${st.cls}">${st.label}</span></td>
        </tr>
      `;
    }).join("");
  }

  function renderVoltages(payload) {
    const t = payload.Telemetry;
    const volts = t.Voltages || [];

    // Destaque das principais trilhas (+12V, +5V, +3.3V, VCore / GPU Core)
    const findRail = (patterns) => volts.find((v) => patterns.some((p) => v.SensorName.toLowerCase().includes(p)));
    const r12 = findRail(["+12v", "12v"]);
    const r5 = findRail(["+5v", "5v"]);
    const rVcore = findRail(["vcore", "cpu core", "vid #1"]);
    const rGpu = volts.find((v) => v.HardwareType === "GPU");

    const highlights = [
      { label: "Linha +12V", item: r12, fallback: "Placa-mãe SuperIO" },
      { label: "Linha +5V", item: r5, fallback: "Placa-mãe SuperIO" },
      { label: "CPU VCore / VID", item: rVcore, fallback: t.CpuName },
      { label: "GPU Core Voltage", item: rGpu, fallback: t.GpuName }
    ];

    $("voltHighlights").innerHTML = highlights.map((h) => `
      <div class="mini-stat-card">
        <div class="mini-stat-label">${esc(h.label)}</div>
        <div class="mini-stat-val green">${h.item ? fmtNum(h.item.Value, 3) + " V" : "-- V"}</div>
        <div class="mini-stat-sub">${esc(h.item ? `${h.item.SensorName} (Mín ${fmtNum(h.item.Min, 3)} / Máx ${fmtNum(h.item.Max, 3)})` : h.fallback)}</div>
      </div>
    `).join("");

    const cat = $("filterVoltCat").value;
    const q = ($("searchVoltage").value || "").trim().toLowerCase();
    const filtered = volts.filter((v) => {
      if (cat !== "ALL" && v.HardwareType !== cat) return false;
      if (q && !v.SensorName.toLowerCase().includes(q) && !v.HardwareName.toLowerCase().includes(q)) return false;
      return true;
    });

    const tbody = $("tblVoltages");
    if (filtered.length === 0) {
      tbody.innerHTML = '<tr><td colspan="7" class="muted">Nenhum sensor de voltagem encontrado.</td></tr>';
      return;
    }

    tbody.innerHTML = filtered.map((v) => {
      const delta = Math.max(0, v.Max - v.Min);
      const atxTag = v.AtxStatus === "ok"
        ? ' <span class="status-tag ok">ATX OK</span>'
        : (v.AtxStatus === "warn" ? ' <span class="status-tag crit">ATX ±5%!</span>' : "");
      return `
        <tr>
          <td><span class="cat-pill ${catClass(v.HardwareType)}">${esc(v.HardwareType)}</span></td>
          <td class="muted">${esc(v.HardwareName)}</td>
          <td><strong>${esc(v.SensorName)}</strong>${atxTag}</td>
          <td class="num ${v.AtxStatus === "warn" ? "red" : "green"}"><strong>${fmtNum(v.Value, 3)} V</strong></td>
          <td class="num muted">${fmtNum(v.Min, 3)} V</td>
          <td class="num muted">${fmtNum(v.Max, 3)} V</td>
          <td class="num ${delta > 0.25 ? "amber" : "muted"}">±${fmtNum(delta, 3)} V</td>
        </tr>
      `;
    }).join("");
  }

  const openSmartDisks = new Set();

  function fmtStorageData(gb) {
    if (gb === null || gb === undefined) return "--";
    if (gb >= 1024) return `${fmtNum(gb / 1024, 2)} TB`;
    return `${fmtNum(gb, 0)} GB`;
  }

  function renderDisks(payload) {
    const container = $("disksContainer");
    if (!container) return;
    const disks = (payload.Telemetry && payload.Telemetry.Disks) || [];
    const cfg = payload.Settings || {};

    if (disks.length === 0) {
      container.innerHTML = `
        <div class="card">
          <div class="card-title">Nenhum disco S.M.A.R.T. detectado</div>
          <div class="card-sub" style="margin-top:6px">Verifique se o VoltPulse está rodando como Administrador (e com driver PawnIO instalado no Windows 11).</div>
        </div>
      `;
      return;
    }

    const circ = 2 * Math.PI * 44; // r = 44 => ~276.46

    container.innerHTML = disks.map((d) => {
      const pct = d.HealthPercent !== null && d.HealthPercent !== undefined ? d.HealthPercent : null;
      let ringCol = "var(--cyan)";
      let badgeCls = "cool";
      if (d.HealthCode === "bad" || (pct !== null && pct < 30)) {
        ringCol = "var(--red)";
        badgeCls = "crit";
      } else if (d.HealthCode === "caution" || (pct !== null && pct < 70)) {
        ringCol = "var(--amber)";
        badgeCls = "warn";
      } else if (d.HealthCode === "good" || (pct !== null && pct >= 70)) {
        ringCol = "var(--green)";
        badgeCls = "ok";
      }

      const dashOffset = pct !== null ? circ - (circ * Math.max(0, Math.min(100, pct))) / 100 : circ * 0.25;
      const tCol = tempColor(d.TemperatureC, 55, 68);
      const daysOn = d.PowerOnHours ? Math.floor(d.PowerOnHours / 24) : 0;
      const usedGb = d.FreeSizeGb !== null && d.FreeSizeGb !== undefined ? Math.max(0, d.TotalSizeGb - d.FreeSizeGb) : null;
      const isOpen = openSmartDisks.has(d.DriveNumber) ? "open" : "";

      const smartRows = (d.SmartAttributes || []).map((a) => `
        <tr>
          <td><code>${esc(a.IdHex)}</code></td>
          <td><strong>${esc(a.Name)}</strong></td>
          <td class="num">${a.Current}</td>
          <td class="num muted">${a.Worst}</td>
          <td class="num muted">${a.Threshold}</td>
          <td class="num cyan">${esc(a.RawValue)}</td>
        </tr>
      `).join("");

      return `
        <div class="card disk-card">
          <div class="disk-top">
            <div class="disk-health-box">
              <div class="disk-ring-wrap">
                <svg class="disk-ring-svg" viewBox="0 0 100 100">
                  <circle class="disk-ring-track" cx="50" cy="50" r="44"></circle>
                  <circle class="disk-ring-fill" cx="50" cy="50" r="44"
                    style="stroke:${ringCol};stroke-dasharray:${circ.toFixed(1)};stroke-dashoffset:${dashOffset.toFixed(1)}"></circle>
                </svg>
                <div class="disk-ring-center">
                  <div class="disk-ring-pct" style="color:${ringCol}">${pct !== null ? pct + "%" : "OK"}</div>
                  <div class="disk-ring-lbl">Vida Útil</div>
                </div>
              </div>
              <span class="status-tag ${badgeCls}">${esc(d.HealthStatus || "Saudável")}</span>
            </div>

            <div class="disk-info-col">
              <div class="disk-title-row">
                <div class="disk-model">${esc(d.Model)}</div>
                <div class="disk-tags">
                  ${d.DriveLetters ? `<span class="disk-tag letter">${esc(d.DriveLetters)}</span>` : ""}
                  <span class="disk-tag">${esc(d.MediaType || "Disco")}</span>
                  <span class="disk-tag">${esc(d.InterfaceType || "")}</span>
                  ${d.Firmware ? `<span class="disk-tag">FW ${esc(d.Firmware)}</span>` : ""}
                  ${d.TrimSupported ? `<span class="disk-tag">TRIM ✓</span>` : ""}
                </div>
              </div>

              <div class="disk-metrics-grid">
                <div class="disk-metric">
                  <span>Temperatura</span>
                  <strong style="color:${tCol}">${d.TemperatureC !== null && d.TemperatureC !== undefined ? fmtNum(d.TemperatureC, 0) + " °C" : "-- °C"}</strong>
                </div>
                <div class="disk-metric">
                  <span>Total Gravado (TBW)</span>
                  <strong class="cyan">${fmtStorageData(d.TotalWrittenGb)}</strong>
                </div>
                <div class="disk-metric">
                  <span>Total Lido</span>
                  <strong>${fmtStorageData(d.TotalReadGb)}</strong>
                </div>
                <div class="disk-metric">
                  <span>Tempo Ligado</span>
                  <strong>${d.PowerOnHours ? fmtNum(d.PowerOnHours, 0) + " h" + (daysOn > 0 ? " (" + daysOn + "d)" : "") : "--"}</strong>
                </div>
                <div class="disk-metric">
                  <span>Nº de Ligações</span>
                  <strong>${d.PowerOnCount ? fmtNum(d.PowerOnCount, 0) + "x" : "--"}</strong>
                </div>
                <div class="disk-metric">
                  <span>Armazenamento (${fmtNum(d.TotalSizeGb, 0)} GB)</span>
                  <strong>${usedGb !== null ? `${fmtNum(usedGb, 0)} GB usados (${fmtNum(d.UsedPercent, 0)}%)` : `${fmtNum(d.TotalSizeGb, 0)} GB`}</strong>
                  ${d.UsedPercent !== null && d.UsedPercent !== undefined ? `<div class="disk-space-bar"><div style="width:${Math.min(100, d.UsedPercent)}%"></div></div>` : ""}
                </div>
              </div>
            </div>
          </div>

          ${(d.SmartAttributes && d.SmartAttributes.length > 0) ? `
            <details class="smart-details" data-disk-num="${d.DriveNumber}" ${isOpen}>
              <summary>▸ Ver atributos S.M.A.R.T. completos (${d.SmartAttributes.length} atributos)</summary>
              <div class="table-wrap" style="max-height:240px">
                <table class="data-table">
                  <thead>
                    <tr>
                      <th>ID</th>
                      <th>Atributo S.M.A.R.T.</th>
                      <th class="num">Atual</th>
                      <th class="num">Pior</th>
                      <th class="num">Limite</th>
                      <th class="num">Valor Bruto</th>
                    </tr>
                  </thead>
                  <tbody>${smartRows}</tbody>
                </table>
              </div>
            </details>
          ` : ""}
        </div>
      `;
    }).join("");

    container.querySelectorAll("details.smart-details").forEach((det) => {
      det.addEventListener("toggle", () => {
        const num = Number(det.dataset.diskNum);
        if (det.open) openSmartDisks.add(num);
        else openSmartDisks.delete(num);
      });
    });
  }

  function fmtDurationSec(sec) {
    const s = Math.max(0, Math.floor(sec || 0));
    const hh = Math.floor(s / 3600);
    const mm = Math.floor((s % 3600) / 60);
    const ss = s % 60;
    return `${String(hh).padStart(2, "0")}:${String(mm).padStart(2, "0")}:${String(ss).padStart(2, "0")}`;
  }

  function fmtNetSpeed(kbps) {
    const v = kbps || 0;
    if (v >= 1024) return `${fmtNum(v / 1024, 2)} MB/s`;
    return `${fmtNum(v, 1)} KB/s`;
  }

  function renderSession(payload) {
    const sess = payload.Session;
    const cfg = payload.Settings || {};
    const cur = cfg.CurrencySymbol || "R$";
    if (!sess) return;

    const pill = $("navSessionPill");
    if (pill) {
      pill.classList.toggle("hidden", !sess.Active);
      pill.classList.toggle("rec", !!sess.Active);
    }

    const btn = $("btnToggleSession");
    if (btn) {
      btn.textContent = sess.Active ? tr("stop_sess") : tr("start_sess");
      btn.className = sess.Active ? "btn btn-danger btn-sm" : "btn btn-primary btn-sm";
    }

    if (!sess.HasLastSession && !sess.Active) return;

    $("sessDur").textContent = fmtDurationSec(sess.ElapsedSeconds);
    $("sessMeta").textContent = sess.Active
      ? `${sess.Name || "Sessão"} • desde ${sess.StartedAt}`
      : `Última: ${sess.Name || "Sessão"} (${sess.StartedAt})`;
    $("sessKwh").textContent = `${fmtNum(sess.EnergyKwh, 3)} kWh`;
    $("sessAvgW").textContent = `Média ${fmtNum(sess.AverageWatts, 0)} W`;
    $("sessCost").textContent = fmtMoney(sess.EnergyKwh * (cfg.TariffPerKwh || 0.85), cur);
    $("sessPeakW").textContent = `Pico ${fmtNum(sess.PeakWatts, 0)} W`;
    const cPk = sess.PeakCpuTempC > 0 ? `${fmtNum(sess.PeakCpuTempC, 0)}°C` : "--°";
    const gPk = sess.PeakGpuTempC > 0 ? `${fmtNum(sess.PeakGpuTempC, 0)}°C` : "--°";
    $("sessTemps").textContent = `${cPk} / ${gPk}`;
  }

  function renderNetwork(t) {
    if (!$("tblNetwork")) return;
    $("netDownBadge").textContent = `↓ ${fmtNetSpeed(t.NetDownloadKBps)}`;
    $("netUpBadge").textContent = `↑ ${fmtNetSpeed(t.NetUploadKBps)}`;
    if (t.ActiveNetworkName) {
      $("netActiveSub").textContent = `Adaptador principal: ${t.ActiveNetworkName}`;
    }

    const nets = t.Networks || [];
    if (nets.length === 0) {
      $("tblNetwork").innerHTML = '<tr><td colspan="5" class="muted">Nenhum adaptador de rede ativo.</td></tr>';
      return;
    }

    $("tblNetwork").innerHTML = nets.map((n) => `
      <tr>
        <td><strong>${esc(n.AdapterName)}</strong></td>
        <td class="num green"><strong>↓ ${fmtNetSpeed(n.DownloadKBps)}</strong></td>
        <td class="num cyan"><strong>↑ ${fmtNetSpeed(n.UploadKBps)}</strong></td>
        <td class="num muted">${n.DataDownloadedGb !== null && n.DataDownloadedGb !== undefined ? fmtNum(n.DataDownloadedGb, 2) + " GB" : "--"}</td>
        <td class="num muted">${n.DataUploadedGb !== null && n.DataUploadedGb !== undefined ? fmtNum(n.DataUploadedGb, 2) + " GB" : "--"}</td>
      </tr>
    `).join("");
  }

  function drawHourlyChart(canvasId, hoursWh, cfg) {
    const canvas = $(canvasId);
    const p = prepCanvas(canvas);
    if (!p || !hoursWh || hoursWh.length !== 24) return;
    const { ctx, w, h } = p;
    const col = themeColors();
    const cur = (cfg && cfg.CurrencySymbol) || "R$";
    const tariff = (cfg && cfg.TariffPerKwh) || 0.85;

    const pad = { left: 16, right: 16, top: 20, bottom: 24 };
    const pw = w - pad.left - pad.right;
    const ph = h - pad.top - pad.bottom;

    const maxWh = Math.max(50, ...hoursWh) * 1.2;
    const slotW = pw / 24;
    const barW = Math.max(8, Math.min(26, slotW * 0.62));
    const curHour = new Date().getHours();

    ctx.strokeStyle = col.grid;
    ctx.beginPath();
    ctx.moveTo(pad.left, pad.top + ph);
    ctx.lineTo(w - pad.right, pad.top + ph);
    ctx.stroke();

    const barsMeta = [];
    for (let hr = 0; hr < 24; hr++) {
      const wh = hoursWh[hr] || 0;
      const cx = pad.left + slotW * hr + slotW / 2;
      const drawH = wh > 0 ? Math.max(4, (wh / maxWh) * ph) : 0;
      const bx = cx - barW / 2;
      const by = pad.top + ph - drawH;

      if (drawH > 0) {
        const grad = ctx.createLinearGradient(0, by, 0, pad.top + ph);
        grad.addColorStop(0, hr === curHour ? col.amber : col.purple);
        grad.addColorStop(1, hr === curHour ? "#f9731688" : "#38bdf866");
        ctx.fillStyle = grad;
        roundRectTop(ctx, bx, by, barW, drawH, 4);
        ctx.fill();
      }

      if (hr % 2 === 0) {
        ctx.font = "10px 'Segoe UI', sans-serif";
        ctx.textAlign = "center";
        ctx.fillStyle = hr === curHour ? col.amber : col.axis;
        ctx.fillText(`${String(hr).padStart(2, "0")}h`, cx, pad.top + ph + 14);
      }

      barsMeta.push({
        x1: bx - 3,
        x2: bx + barW + 3,
        hour: hr,
        wh,
        kwh: wh / 1000,
        cost: (wh / 1000) * tariff,
        cur
      });
    }

    state.hoverCharts[canvasId] = { type: "hourly", bars: barsMeta };
  }

  function renderSystem(payload) {
    const t = payload.Telemetry;
    const specs = [
      { label: "Processador (CPU)", val: t.CpuName, sub: t.CpuClockMhz ? `Clock atual: ${fmtNum(t.CpuClockMhz, 0)} MHz` : "Monitoramento ativo" },
      { label: "Placa de Vídeo (GPU)", val: t.GpuName, sub: t.GpuVramTotalMb ? `VRAM: ${fmtNum(t.GpuVramUsedMb || 0, 0)} / ${fmtNum(t.GpuVramTotalMb, 0)} MB` : "Monitoramento ativo" },
      { label: "Placa-mãe", val: t.MotherboardName, sub: `${(t.Voltages || []).length} sensores de voltagem` },
      { label: "Memória & Discos", val: t.RamTotalGb ? `${fmtNum(t.RamTotalGb, 0)} GB RAM` : "Memória", sub: (t.StorageNames && t.StorageNames.length) ? t.StorageNames.join(" • ") : "Sem discos listados" }
    ];

    $("sysSpecsGrid").innerHTML = specs.map((s) => `
      <div class="mini-stat-card">
        <div class="mini-stat-label">${esc(s.label)}</div>
        <div class="mini-stat-val" style="font-size:15px;line-height:1.3">${esc(s.val)}</div>
        <div class="mini-stat-sub">${esc(s.sub)}</div>
      </div>
    `).join("");

    const fans = t.Fans || [];
    $("tblFans").innerHTML = fans.length === 0
      ? '<tr><td colspan="4" class="muted">Nenhuma ventoinha reportada.</td></tr>'
      : fans.map((f) => `
          <tr>
            <td><span class="cat-pill ${catClass(f.HardwareType)}">${esc(f.HardwareType)}</span></td>
            <td>${esc(f.SensorName)}</td>
            <td class="num cyan"><strong>${fmtNum(f.Value, 0)} ${esc(f.Unit)}</strong></td>
            <td class="num muted">${fmtNum(f.Max, 0)} ${esc(f.Unit)}</td>
          </tr>
        `).join("");

    const cl = [...(t.Loads || []), ...(t.Clocks || []).slice(0, 12)];
    $("tblClocksLoads").innerHTML = cl.length === 0
      ? '<tr><td colspan="4" class="muted">Sem dados de clocks/cargas.</td></tr>'
      : cl.map((c) => `
          <tr>
            <td><span class="cat-pill ${catClass(c.HardwareType)}">${esc(c.HardwareType)}</span></td>
            <td>${esc(c.SensorName)}</td>
            <td class="num"><strong>${fmtNum(c.Value, c.Unit === "%" ? 1 : 0)} ${esc(c.Unit)}</strong></td>
            <td class="num muted">${fmtNum(c.Max, c.Unit === "%" ? 1 : 0)} ${esc(c.Unit)}</td>
          </tr>
        `).join("");
  }

  // ==================== HiDPI INTERACTIVE CHARTS ====================
  function prepCanvas(canvas) {
    if (!canvas) return null;
    const rect = canvas.getBoundingClientRect();
    if (rect.width < 10 || rect.height < 10) return null;
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.round(rect.width * dpr);
    canvas.height = Math.round(rect.height * dpr);
    const ctx = canvas.getContext("2d");
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    return { ctx, w: rect.width, h: rect.height };
  }

  function themeColors() {
    const light = document.documentElement.getAttribute("data-theme") === "light";
    return {
      grid: light ? "rgba(15,23,42,0.08)" : "rgba(148,163,184,0.12)",
      axis: light ? "#64748b" : "#94a3b8",
      cyan: light ? "#0284c7" : "#38bdf8",
      green: light ? "#059669" : "#34d399",
      purple: light ? "#7c3aed" : "#a78bfa",
      amber: light ? "#d97706" : "#fbbf24",
      rose: light ? "#e11d48" : "#fb7185"
    };
  }

  function drawLineChart(canvasId, data, seriesCfg, unit, minMaxFloor) {
    const canvas = $(canvasId);
    const p = prepCanvas(canvas);
    if (!p) return;
    const { ctx, w, h } = p;
    const col = themeColors();

    const pad = { left: 46, right: 14, top: 14, bottom: 24 };
    const pw = w - pad.left - pad.right;
    const ph = h - pad.top - pad.bottom;

    let peak = minMaxFloor;
    for (const pt of data) {
      for (const s of seriesCfg) {
        const v = pt[s.key];
        if (v !== null && v !== undefined && v > peak) peak = v;
      }
    }
    const maxVal = Math.ceil((peak * 1.18) / 10) * 10;

    // Grid
    ctx.font = "11px 'Segoe UI', sans-serif";
    ctx.textAlign = "right";
    ctx.textBaseline = "middle";
    const steps = 4;
    for (let i = 0; i <= steps; i++) {
      const y = pad.top + ph - (ph * i) / steps;
      ctx.strokeStyle = col.grid;
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(pad.left, y);
      ctx.lineTo(w - pad.right, y);
      ctx.stroke();

      ctx.fillStyle = col.axis;
      ctx.fillText(`${Math.round((maxVal * i) / steps)}${unit}`, pad.left - 8, y);
    }

    if (data.length < 2) {
      ctx.textAlign = "center";
      ctx.fillStyle = col.axis;
      ctx.fillText("Coletando amostras ao vivo...", pad.left + pw / 2, pad.top + ph / 2);
      return;
    }

    const dx = pw / Math.max(1, state.maxPoints - 1);
    const startX = pad.left + pw - dx * (data.length - 1);

    for (const s of seriesCfg) {
      const pts = [];
      for (let i = 0; i < data.length; i++) {
        const val = data[i][s.key];
        if (val === null || val === undefined) continue;
        const x = startX + i * dx;
        const y = pad.top + ph - Math.max(0, Math.min(1, val / maxVal)) * ph;
        pts.push({ x, y });
      }
      if (pts.length < 2) continue;

      // Area fill
      if (s.fill) {
        const grad = ctx.createLinearGradient(0, pad.top, 0, pad.top + ph);
        grad.addColorStop(0, s.color + "38");
        grad.addColorStop(1, s.color + "02");
        ctx.beginPath();
        ctx.moveTo(pts[0].x, pts[0].y);
        for (let i = 1; i < pts.length; i++) ctx.lineTo(pts[i].x, pts[i].y);
        ctx.lineTo(pts[pts.length - 1].x, pad.top + ph);
        ctx.lineTo(pts[0].x, pad.top + ph);
        ctx.closePath();
        ctx.fillStyle = grad;
        ctx.fill();
      }

      // Stroke
      ctx.beginPath();
      ctx.moveTo(pts[0].x, pts[0].y);
      for (let i = 1; i < pts.length; i++) ctx.lineTo(pts[i].x, pts[i].y);
      ctx.strokeStyle = s.color;
      ctx.lineWidth = s.width || 2;
      ctx.lineJoin = "round";
      ctx.stroke();

      // End dot
      const last = pts[pts.length - 1];
      ctx.beginPath();
      ctx.arc(last.x, last.y, 3.2, 0, Math.PI * 2);
      ctx.fillStyle = s.color;
      ctx.fill();
    }

    // Hover metadata
    state.hoverCharts[canvasId] = {
      type: "line",
      data,
      seriesCfg,
      unit,
      pad,
      startX,
      dx
    };
  }

  function drawBarChart(canvasId, days, cfg) {
    const canvas = $(canvasId);
    const p = prepCanvas(canvas);
    if (!p || !days || days.length === 0) return;
    const { ctx, w, h } = p;
    const col = themeColors();
    const cur = (cfg && cfg.CurrencySymbol) || "R$";
    const tariff = (cfg && cfg.TariffPerKwh) || 0.85;

    const pad = { left: 16, right: 16, top: 24, bottom: 26 };
    const pw = w - pad.left - pad.right;
    const ph = h - pad.top - pad.bottom;

    const maxKwh = Math.max(0.2, ...days.map((d) => d.EnergyKwh || 0)) * 1.25;
    const slotW = pw / days.length;
    const barW = Math.max(12, Math.min(38, slotW * 0.54));

    ctx.strokeStyle = col.grid;
    ctx.beginPath();
    ctx.moveTo(pad.left, pad.top + ph);
    ctx.lineTo(w - pad.right, pad.top + ph);
    ctx.stroke();

    const barsMeta = [];
    days.forEach((d, i) => {
      const cx = pad.left + slotW * i + slotW / 2;
      const bh = ((d.EnergyKwh || 0) / maxKwh) * ph;
      const drawH = (d.EnergyKwh || 0) > 0 ? Math.max(4, bh) : 0;
      const bx = cx - barW / 2;
      const by = pad.top + ph - drawH;
      const isToday = i === days.length - 1;

      if (drawH > 0) {
        const grad = ctx.createLinearGradient(0, by, 0, pad.top + ph);
        grad.addColorStop(0, isToday ? col.green : col.cyan);
        grad.addColorStop(1, isToday ? "#10b98188" : "#6366f188");
        ctx.fillStyle = grad;
        roundRectTop(ctx, bx, by, barW, drawH, 5);
        ctx.fill();
      }

      // Label top (apenas quando cabe)
      ctx.font = "10.5px 'Segoe UI', sans-serif";
      ctx.textAlign = "center";
      ctx.fillStyle = col.axis;
      if (days.length <= 7 && (d.EnergyKwh || 0) > 0) {
        ctx.fillText(`${fmtNum(d.EnergyKwh, 2)}`, cx, Math.max(12, by - 8));
      }

      // Label bottom
      const lbl = isToday ? "Hoje" : formatShortDate(d.Date);
      ctx.fillText(lbl, cx, pad.top + ph + 15);

      barsMeta.push({ x1: bx - 4, x2: bx + barW + 4, day: d, cost: (d.EnergyKwh || 0) * tariff, cur });
    });

    state.hoverCharts[canvasId] = { type: "bar", bars: barsMeta };
  }

  function roundRectTop(ctx, x, y, w, h, r) {
    const rad = Math.min(r, w / 2, h);
    ctx.beginPath();
    ctx.moveTo(x, y + h);
    ctx.lineTo(x, y + rad);
    ctx.quadraticCurveTo(x, y, x + rad, y);
    ctx.lineTo(x + w - rad, y);
    ctx.quadraticCurveTo(x + w, y, x + w, y + rad);
    ctx.lineTo(x + w, y + h);
    ctx.closePath();
  }

  function attachChartHover(canvasId) {
    const canvas = $(canvasId);
    const tip = $("chartTooltip");
    canvas.addEventListener("mousemove", (ev) => {
      const meta = state.hoverCharts[canvasId];
      if (!meta) return;
      const rect = canvas.getBoundingClientRect();
      const mx = ev.clientX - rect.left;

      if (meta.type === "line" && meta.data.length > 0) {
        const idx = Math.round((mx - meta.startX) / meta.dx);
        if (idx < 0 || idx >= meta.data.length) {
          tip.classList.add("hidden");
          return;
        }
        const pt = meta.data[idx];
        const rows = meta.seriesCfg
          .map((s) => {
            const v = pt[s.key];
            return `<div><span style="color:${s.color}">●</span> ${s.label}: <strong>${v !== null && v !== undefined ? fmtNum(v, 1) + meta.unit : "--"}</strong></div>`;
          })
          .join("");
        tip.innerHTML = `<div style="color:#94a3b8;margin-bottom:3px">${pt.time}</div>${rows}`;
        positionTooltip(tip, ev.clientX, ev.clientY);
      } else if (meta.type === "hourly") {
        const hit = meta.bars.find((b) => mx >= b.x1 && mx <= b.x2);
        if (!hit) {
          tip.classList.add("hidden");
          return;
        }
        tip.innerHTML = `
          <div style="color:#94a3b8;margin-bottom:3px"><strong>${String(hit.hour).padStart(2, "0")}:00 – ${String(hit.hour).padStart(2, "0")}:59</strong></div>
          <div>Consumo: <strong style="color:#38bdf8">${fmtNum(hit.wh, 1)} Wh (${fmtNum(hit.kwh, 3)} kWh)</strong></div>
          <div>Custo: <strong style="color:#34d399">${fmtMoney(hit.cost, hit.cur)}</strong></div>
        `;
        positionTooltip(tip, ev.clientX, ev.clientY);
      } else if (meta.type === "bar") {
        const hit = meta.bars.find((b) => mx >= b.x1 && mx <= b.x2);
        if (!hit) {
          tip.classList.add("hidden");
          return;
        }
        const d = hit.day;
        tip.innerHTML = `
          <div style="color:#94a3b8;margin-bottom:3px"><strong>${formatDateBr(d.Date)}</strong></div>
          <div>Consumo: <strong style="color:#38bdf8">${fmtNum(d.EnergyKwh, 3)} kWh</strong></div>
          <div>Custo: <strong style="color:#34d399">${fmtMoney(hit.cost, hit.cur)}</strong></div>
          <div>Média / Pico: <strong>${fmtNum(d.AverageWatts, 0)}W / ${fmtNum(d.PeakWatts, 0)}W</strong></div>
        `;
        positionTooltip(tip, ev.clientX, ev.clientY);
      }
    });

    canvas.addEventListener("mouseleave", () => tip.classList.add("hidden"));
  }

  function positionTooltip(tip, cx, cy) {
    tip.classList.remove("hidden");
    const tw = tip.offsetWidth;
    const th = tip.offsetHeight;
    const x = cx + 14 + tw > window.innerWidth ? cx - tw - 14 : cx + 14;
    const y = cy + 14 + th > window.innerHeight ? cy - th - 14 : cy + 14;
    tip.style.left = `${x}px`;
    tip.style.top = `${y}px`;
  }

  function redrawAllCharts() {
    const col = themeColors();
    drawLineChart("chartLivePower", state.powerSeries, [
      { key: "wall", label: "Tomada", color: col.cyan, fill: true, width: 2.3 },
      { key: "gpu", label: "GPU", color: col.green, fill: false, width: 1.8 },
      { key: "cpu", label: "CPU", color: col.purple, fill: false, width: 1.8 }
    ], "W", 80);

    drawLineChart("chartLiveTemps", state.tempSeries, [
      { key: "cpu", label: "CPU", color: col.amber, fill: true, width: 2.2 },
      { key: "gpu", label: "GPU", color: col.rose, fill: true, width: 2.2 }
    ], "°C", 70);

    if (state.payload && state.payload.Summary) {
      const days14 = state.payload.Summary.Last14Days || [];
      drawBarChart("chartOverviewBars", days14.slice(-7), state.payload.Settings);
      drawBarChart("chartEnergy14Days", days14, state.payload.Settings);
      drawHourlyChart("chartHourlyToday", state.payload.Summary.HourlyWhToday, state.payload.Settings);
    }
  }

  ["chartLivePower", "chartLiveTemps", "chartOverviewBars", "chartEnergy14Days", "chartHourlyToday"].forEach(attachChartHover);
  window.addEventListener("resize", () => redrawAllCharts());

  function formatDateBr(iso) {
    if (!iso || iso.length < 10) return iso || "";
    const [y, m, d] = iso.split("-");
    return `${d}/${m}/${y}`;
  }

  function formatShortDate(iso) {
    if (!iso || iso.length < 10) return iso || "";
    const [, m, d] = iso.split("-");
    return `${d}/${m}`;
  }

  function esc(str) {
    return String(str || "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  // ==================== HOST BRIDGE OR DEMO FALLBACK ====================
  if (isHost) {
    window.chrome.webview.addEventListener("message", (event) => {
      if (event.data && event.data.Type === "telemetry") {
        render(event.data);
      }
    });
    sendHost({ action: "ready" });
  } else {
    // Modo preview no navegador (dados simulados realistas para desenvolvimento/screenshots)
    let tick = 0;
    const demoDays = [];
    const today = new Date();
    for (let i = 13; i >= 0; i--) {
      const d = new Date(today);
      d.setDate(today.getDate() - i);
      const iso = d.toISOString().slice(0, 10);
      const wh = i === 0 ? 740 : 650 + Math.round(Math.sin(i * 1.1) * 280 + 420);
      demoDays.push({
        Date: iso,
        EnergyWh: wh,
        EnergyKwh: wh / 1000,
        ActiveSeconds: 21000 + (i % 4) * 3600,
        PeakWatts: 235 + (i % 5) * 14,
        AverageWatts: 138 + (i % 3) * 18
      });
    }

    const pushDemo = () => {
      tick++;
      const cpuW = 28 + Math.sin(tick * 0.4) * 9 + Math.random() * 4;
      const gpuW = 58 + Math.cos(tick * 0.3) * 18 + Math.random() * 5;
      const wallW = (cpuW + gpuW + 30) / 0.85;
      const cpuT = 51 + Math.sin(tick * 0.35) * 5;
      const gpuT = 48 + Math.cos(tick * 0.25) * 4;

      const demo = {
        Type: "telemetry",
        AppVersion: "2.0.0",
        DataDirectory: "C:\\Users\\Demo\\AppData\\Local\\PcPowerMonitor",
        Settings: {
          TariffPerKwh: 0.89,
          CurrencySymbol: "R$",
          PsuEfficiencyPercent: 85,
          BaseSystemWatts: 30,
          PollIntervalSeconds: 2,
          TempWarningC: 75,
          TempCriticalC: 88,
          MinimizeToTrayOnClose: true,
          StartWithWindows: false,
          Theme: document.documentElement.getAttribute("data-theme") || "dark"
        },
        Telemetry: {
          TimeLabel: new Date().toLocaleTimeString("pt-BR"),
          IsAdmin: true,
          PawnIoInstalled: true,
          Paused: false,
          CpuName: "12th Gen Intel Core i5-12400F",
          GpuName: "AMD Radeon RX 580 2048SP",
          MotherboardName: "H610M-GAMING D4",
          StorageNames: ["NVMe Kingston SNV2S 1TB"],
          CpuWatts: cpuW,
          GpuWatts: gpuW,
          OtherWatts: 0,
          ComponentsTotalWatts: cpuW + gpuW,
          EstimatedWallWatts: wallW,
          CpuTempC: cpuT,
          GpuTempC: gpuT,
          GpuHotspotC: gpuT + 9.4,
          MaxStorageTempC: 39.0,
          CpuLoadPercent: 26 + Math.sin(tick * 0.5) * 12,
          GpuLoadPercent: 34 + Math.cos(tick * 0.3) * 15,
          RamLoadPercent: 54.2,
          RamUsedGb: 8.7,
          RamTotalGb: 16.0,
          GpuVramUsedMb: 2140,
          GpuVramTotalMb: 8192,
          CpuClockMhz: 4000,
          GpuClockMhz: 1284,
          GpuFanRpm: 1420,
          MaxFanRpm: 1420,
          PowerSensors: [
            { HardwareType: "CPU", HardwareName: "Intel i5-12400F", SensorName: "CPU Package", Value: cpuW, Min: 14, Max: 65, Unit: "W" },
            { HardwareType: "GPU", HardwareName: "AMD RX 580", SensorName: "GPU Package", Value: gpuW, Min: 22, Max: 148, Unit: "W" }
          ],
          Temperatures: [
            { HardwareType: "CPU", HardwareName: "Intel i5-12400F", SensorName: "CPU Package", Value: cpuT, Min: 38, Max: 69, Unit: "°C" },
            { HardwareType: "GPU", HardwareName: "AMD RX 580", SensorName: "GPU Core", Value: gpuT, Min: 35, Max: 74, Unit: "°C" },
            { HardwareType: "GPU", HardwareName: "AMD RX 580", SensorName: "GPU Hot Spot", Value: gpuT + 9.4, Min: 42, Max: 83, Unit: "°C" },
            { HardwareType: "Placa-mãe", HardwareName: "H610M-GAMING D4", SensorName: "VRM MOS", Value: 44.5, Min: 36, Max: 54, Unit: "°C" },
            { HardwareType: "Armazenamento", HardwareName: "NVMe Kingston 1TB", SensorName: "Temperature", Value: 39.0, Min: 34, Max: 44, Unit: "°C" }
          ],
          Voltages: [
            { HardwareType: "Placa-mãe", HardwareName: "H610M-GAMING D4", SensorName: "+12V", Value: 12.048, Min: 11.98, Max: 12.11, Unit: "V" },
            { HardwareType: "Placa-mãe", HardwareName: "H610M-GAMING D4", SensorName: "+5V", Value: 5.04, Min: 5.01, Max: 5.06, Unit: "V" },
            { HardwareType: "Placa-mãe", HardwareName: "H610M-GAMING D4", SensorName: "+3.3V", Value: 3.312, Min: 3.296, Max: 3.328, Unit: "V" },
            { HardwareType: "CPU", HardwareName: "Intel i5-12400F", SensorName: "CPU VCore", Value: 1.142, Min: 0.785, Max: 1.245, Unit: "V" },
            { HardwareType: "GPU", HardwareName: "AMD RX 580", SensorName: "GPU Core", Value: 1.037, Min: 0.75, Max: 1.15, Unit: "V" }
          ],
          Fans: [
            { HardwareType: "GPU", HardwareName: "AMD RX 580", SensorName: "GPU Fan", Value: 1420, Min: 0, Max: 2400, Unit: "RPM" },
            { HardwareType: "Placa-mãe", HardwareName: "H610M-GAMING D4", SensorName: "CPU Fan", Value: 1180, Min: 850, Max: 1650, Unit: "RPM" }
          ],
          Clocks: [
            { HardwareType: "CPU", HardwareName: "Intel i5-12400F", SensorName: "Core #1", Value: 4000, Min: 800, Max: 4400, Unit: "MHz" },
            { HardwareType: "GPU", HardwareName: "AMD RX 580", SensorName: "GPU Core", Value: 1284, Min: 300, Max: 1284, Unit: "MHz" }
          ],
          Loads: [
            { HardwareType: "CPU", HardwareName: "Intel i5-12400F", SensorName: "CPU Total", Value: 26.4, Min: 3, Max: 89, Unit: "%" },
            { HardwareType: "GPU", HardwareName: "AMD RX 580", SensorName: "GPU Core", Value: 34.1, Min: 0, Max: 99, Unit: "%" },
            { HardwareType: "Memória", HardwareName: "Generic Memory", SensorName: "Memory", Value: 54.2, Min: 42, Max: 78, Unit: "%" }
          ]
        },
        Summary: {
          TodayKwh: 0.74,
          TodayCost: 0.74 * 0.89,
          TodayActiveHours: 5.2,
          TodayPeakWatts: 248,
          TodayAvgWatts: 142,
          WeekKwh: 7.42,
          WeekCost: 7.42 * 0.89,
          WeekAvgDailyKwh: 1.06,
          WeekDaysRecorded: 7,
          MonthKwh: 22.8,
          MonthCost: 22.8 * 0.89,
          ProjectedMonthKwh: 31.8,
          ProjectedMonthCost: 31.8 * 0.89,
          MonthDaysRecorded: 21,
          Last14Days: demoDays,
          AllDays: [...demoDays].reverse()
        }
      };
      render(demo);
    };

    pushDemo();
    setInterval(pushDemo, 2000);
  }
})();
