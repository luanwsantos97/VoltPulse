# Changelog

## [3.2.1] - 2026-11-23
### Corrigido & Melhorado
- **Correção crítica de inicialização na UI (`ui/app.js`)**: corrigido erro `ReferenceError` na ordem de declaração do helper `$` que impedia a dashboard de sair de *"Detectando componentes do PC..."*.
- **Splash Screen integrado suave e leve**: a janela abre instantaneamente com animação de progresso em 3 etapas enquanto os sensores são inicializados em segundo plano (`Task.Run`), revelando a dashboard já preenchida assim que o primeiro snapshot chega.
- **Fallback WMI universal (`Win32_Processor`, `Win32_VideoController`, `Win32_BaseBoard`, `Win32_ComputerSystem`)**: garante identificação precisa de CPU, GPU física (filtrando adaptadores virtuais), placa-mãe e RAM total em qualquer PC ou notebook Windows.

## [3.2.0] - 2026-11-23
### Adicionado
- **Instalador Windows Profissional (`VoltPulse-Setup-v3.2.0.exe`)** via Inno Setup 6 (`installer/VoltPulse.iss`):
  - Assistente multilíngue (PT-BR, EN, ES, FR, DE, IT, JA) com ícone próprio e licença MIT.
  - Instalação padrão em `C:\Program Files (x86)\VoltPulse` (personalizável pelo usuário).
  - Registro completo no Windows (`HKLM\SOFTWARE\VoltPulse`, `App Paths` para abrir via `Win + R → voltpulse`, e entrada oficial em **Apps & Recursos** com desinstalador limpo).
  - Criação de atalhos no **Menu Iniciar** e **Área de Trabalho**.
  - Instalação automática condicional das dependências (**PawnIO** + **Edge WebView2 Runtime**) durante o setup.
  - Opção de configurar **Iniciar com o Windows sem UAC** diretamente na instalação (e remoção limpa na desinstalação).
- Flag `-Installer` adicionada ao `build.ps1` e compilação automática do instalador `.exe` no GitHub Actions.

## [3.1.0] - 2026-11-23
### Adicionado
- **Detecção automática de País, Moeda e Tarifa Inicial pelo Windows** (`RegionInfo.CurrentRegion`): suporta automaticamente `BRL`, `USD`, `EUR`, `GBP`, `CAD`, `AUD`, `CHF`, `MXN`, `ARS`, `JPY`, `CNY`, `INR`, `KRW`, `PLN` e qualquer outra moeda local, com 14 presets globais e botão **"Auto-detectar do Windows"** nas Configurações.
- **Suporte a 8 idiomas** em módulo dedicado [`ui/i18n.js`](ui/i18n.js): Português (BR), English (US), Español, Français, Deutsch, Italiano, 日本語 e 简体中文 — com detecção automática pelo idioma do Windows e formatação numérica localizada.
- **Estrutura aberta a contribuições**: `.github/PULL_REQUEST_TEMPLATE.md`, `CODE_OF_CONDUCT.md` e guia de como contribuir novos idiomas em `CONTRIBUTING.md`.

## [3.0.0] - 2026-11-23
### Adicionado
- **Ícone `.ico` vetorial multi-resolução** (`16/32/48/256px`) embutido diretamente no `VoltPulse.exe`.
- **Iniciar com o Windows sem pop-up de UAC** via Agendador de Tarefas (`RunLevel HIGHEST`).
- **Mini Widget Flutuante (Overlay)** compacto, semi-transparente, arrastável e sempre no topo (`W • CPU °C • GPU °C • RAM %`).
- **Alertas Inteligentes (Notificações do Windows)** para temperatura crítica (CPU/GPU), trilhas ATX (`+12V`, `+5V`, `+3.3V`) fora da faixa de ±5% e meta de gasto mensal.
- **Medidor de Sessão / Jogo** (duração, kWh, custo, média/pico W e picos térmicos CPU/GPU).
- **Gráfico de Consumo por Hora (Hoje 00h–23h)** na aba Energia & Custos.
- **Monitoramento de Rede em Tempo Real** (Download / Upload em KB/s e MB/s por adaptador) na aba Sistema & Rede.
- **Suporte a Idiomas PT-BR / EN-US** com detecção automática e troca instantânea.
- **GitHub Actions CI/CD** (`.github/workflows/release.yml`) para build automático e publicação em GitHub Releases via tag `v*`.

## [2.1.0] - 2026-11-23
### Adicionado
- **Saúde & Vida Útil de SSD / NVMe / HD (estilo CrystalDiskInfo)**: nova aba dedicada `Discos & SSD` com gauge circular de **Vida Útil (%)**, status (`Saudável` / `Atenção` / `Crítico`), temperatura, total gravado (**TBW**), total lido, tempo ligado (horas/dias), nº de ligações, barra de espaço ocupado e tabela expansível de **atributos S.M.A.R.T.**.
- **Assistente de Instalação Automática de Dependências na abertura**: detecta automaticamente se o driver **PawnIO** ou o **Microsoft Edge WebView2 Runtime** estão ausentes e instala em 1 clique (inclusive com `PawnIO_setup.exe` incluído no pacote de release para instalação offline).
- Adicionada `BlackSharp.Core.dll` e registro de logs de diagnóstico em `crash.log`.

## [2.0.0] - 2026-11-23
### Adicionado
- Nova interface moderna **VoltPulse** construída sobre **Edge WebView2** nativo com barra de título escura no Windows 10/11.
- Tema **Escuro (Dark Slate)** e **Claro (Clean Light)** com alternância rápida.
- **Gauges circulares SVG** animados para Temperatura CPU, Temperatura GPU, Potência na Tomada e Memória RAM.
- **Monitoramento completo de hardware**: Temperaturas (CPU, GPU, Hot Spot, Placa-mãe, SSD/HD), Voltagens (com variação Δ), Ventoinhas (RPM/%), Clocks (MHz) e Cargas (%).
- **Gráficos HiDPI interativos** com tooltip ao passar o mouse (Potência ao vivo, Temperaturas ao vivo e Barras de consumo de 14 dias).
- **Simulador interativo de conta de luz** na aba Energia & Custos.
- Exportação de relatório em **CSV** com diálogo nativo de salvar.
- Detecção automática de privilégios de Administrador e do driver **PawnIO**.
- Estrutura completa pronta para GitHub (`scripts/fetch-deps.ps1`, `-ReleaseZip`, `LICENSE`, `.gitignore`, `CONTRIBUTING.md`).

## [1.0.0] - 2026-11-23
- Versão inicial desktop em WinForms com monitoramento de voltagens, consumo em Watts e histórico diário/semanal/mensal.
