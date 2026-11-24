# Contributing to VoltPulse / Contribuindo com o VoltPulse

VoltPulse is an open-source community project — Pull Requests, translations, new hardware sensors, and bug reports are always welcome!

---

## 🇺🇸 English

### How to Contribute Code
1. **Fork** the repository (`https://github.com/luanwsantos97/VoltPulse`).
2. Clone your fork and create a branch:
   ```bash
   git checkout -b feat/your-feature-name
   ```
3. Make your edits in `src/` (C# backend) or `ui/` (HTML/CSS/JS dashboard).
4. Validate syntax and build cleanly on Windows:
   ```powershell
   node --check ui/i18n.js
   node --check ui/app.js
   powershell -ExecutionPolicy Bypass -File .\build.ps1
   ```
5. Push your branch and open a **Pull Request** using the PR template.

### 🌍 How to Add a New Language (Easy First Contribution!)
VoltPulse already supports **8 languages** (`pt-BR`, `en-US`, `es-ES`, `fr-FR`, `de-DE`, `it-IT`, `ja-JP`, `zh-CN`). To add another (e.g., Korean, Polish, Russian, Turkish):
1. Open [`ui/i18n.js`](ui/i18n.js).
2. Add your language entry to `VOLTPULSE_LANGUAGES` and copy the `"en-US"` block in `VOLTPULSE_I18N` with your translations.
3. Add your language code to `AppSettings.DetectSystemLanguage()` and `AppSettings.NormalizeLanguage()` in [`src/Models.cs`](src/Models.cs).
4. Open a Pull Request — even without installing Visual Studio, GitHub Actions will automatically verify the build!

### 💱 How to Add a Country Currency Preset
Add a new entry in `VOLTPULSE_CURRENCIES` inside [`ui/i18n.js`](ui/i18n.js) and its default tariff in `AppSettings.GetDefaultTariffForCurrency()` inside [`src/Models.cs`](src/Models.cs).

---

## 🇧🇷 Português (Brasil)

O VoltPulse é um projeto open-source aberto à comunidade — Pull Requests, novas traduções, sensores e correções são sempre bem-vindos!

### Como Contribuir com Código
1. Faça um **Fork** do repositório (`https://github.com/luanwsantos97/VoltPulse`).
2. Crie uma branch para sua mudança:
   ```bash
   git checkout -b feat/sua-melhoria
   ```
3. Edite os arquivos em `src/` (backend C#) ou `ui/` (interface HTML/CSS/JS).
4. Valide o JS e compile:
   ```powershell
   node --check ui/i18n.js
   node --check ui/app.js
   powershell -ExecutionPolicy Bypass -File .\build.ps1
   ```
5. Envie sua branch e abra um **Pull Request** (o GitHub Actions valida automaticamente a compilação!).

### 🌍 Como Adicionar um Novo Idioma
Basta adicionar o bloco traduzido em [`ui/i18n.js`](ui/i18n.js) e registrar o código do idioma em `AppSettings.NormalizeLanguage()` em [`src/Models.cs`](src/Models.cs).
