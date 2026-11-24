# Contributing to VoltPulse / Contribuindo com o VoltPulse

## 🇺🇸 English

Contributions are very welcome! Whether it's a bug fix, a new sensor integration, UI polish, or translation improvements:

1. **Fork** the repository and create a feature branch:
   ```bash
   git checkout -b feat/your-feature-name
   ```
2. Make your changes in `src/` (C# backend) or `ui/` (HTML/CSS/JS dashboard).
3. Validate JavaScript syntax and compile cleanly:
   ```powershell
   node --check ui/app.js
   powershell -ExecutionPolicy Bypass -File .\build.ps1
   ```
4. Open a **Pull Request** explaining what changed (screenshots are greatly appreciated for UI updates).

> **UI Tip:** You can open `ui/index.html` directly in your browser to test visual changes with built-in simulated telemetry.

---

## 🇧🇷 Português (Brasil)

Contribuições são muito bem-vindas! Seja uma correção de bug, novo sensor, melhoria visual ou tradução:

1. Faça um **Fork** do repositório e crie uma branch:
   ```bash
   git checkout -b feat/sua-melhoria
   ```
2. Faça suas alterações em `src/` (backend C#) ou `ui/` (dashboard HTML/CSS/JS).
3. Valide a sintaxe JS e compile sem erros:
   ```powershell
   node --check ui/app.js
   powershell -ExecutionPolicy Bypass -File .\build.ps1
   ```
4. Abra um **Pull Request** descrevendo a mudança (prints ajudam bastante em alterações visuais).

> **Dica de UI:** Abra `ui/index.html` direto no navegador para testar o visual rapidamente usando o modo de demonstração automático.
