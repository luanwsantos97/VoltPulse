# Security Policy / Política de Segurança

## English

### Why does VoltPulse require Administrator privileges?
VoltPulse reads low-level hardware telemetry (CPU RAPL energy counters, motherboard SuperIO voltage/fan sensors, and physical NVMe/SATA S.M.A.R.T. registers). Windows restricts access to these hardware interfaces to elevated processes and signed kernel drivers (such as **PawnIO**, replacing legacy vulnerable WinRing0 / `CVE-2020-14979`).

### Built-In Security Controls
- **Zero External Telemetry**: VoltPulse never sends hardware or usage data anywhere.
- **Locked-Down WebView2 + CSP**: Loads strictly from an in-memory local virtual host (`https://voltpulse.local/`), blocks all external navigations/popups, enforces a strict `Content-Security-Policy` (`script-src 'self'`), and restricts `openUrl` IPC calls to an HTTPS allowlist.
- **Signed Dependencies & SHA-256 Verified Updates**: Verifies Win32 Authenticode digital signatures (`WinVerifyTrust`) before running dependency installers, verifies published **`SHA256SUMS.txt`** hashes in the auto-updater, and isolates downloads in random temporary directories.
- **DLL & PATH Hijacking Mitigations**: System Win32/ADL P/Invokes enforce `DllImportSearchPath.System32`, and all OS utilities (`powercfg.exe`, `powershell.exe`, `schtasks.exe`, `taskkill.exe`, `explorer.exe`) are invoked via explicit absolute system paths.
- **Hardened Optional Local API**: Disabled by default, binds strictly to `127.0.0.1`, validates the `Host` header (anti-DNS-rebinding), removes wildcard CORS, accepts only `GET`/`HEAD`, and strips local filesystem paths (`DataDirectory`).
- **Auditable Builds**: Built from source via least-privilege GitHub Actions (`.github/workflows/release.yml`) or `build.ps1`, publishing `SHA256SUMS.txt` on every release.

### Reporting a Vulnerability
If you discover a security issue, please open a private security advisory on GitHub or contact the repository maintainer before public disclosure.

---

## Português (Brasil)

### Por que o VoltPulse precisa de permissão de Administrador?
O VoltPulse lê telemetria de hardware em baixo nível (contadores de energia RAPL da CPU, sensores SuperIO de voltagem/ventoinhas da placa-mãe e registradores S.M.A.R.T. físicos dos SSDs/HDs). O Windows restringe esse acesso a processos elevados e drivers assinados (como o **PawnIO**, substituindo o antigo WinRing0 / `CVE-2020-14979`).

### Controles de Segurança Integrados
- **Zero Telemetria Externa**: nenhum dado de hardware ou consumo sai do seu PC.
- **WebView2 Blindado + CSP**: carrega exclusivamente do host virtual local em memória (`https://voltpulse.local/`), bloqueia navegações externas e popups, aplica `Content-Security-Policy` restritivo (`script-src 'self'`) e limita links externos a uma allowlist HTTPS.
- **Assinatura Authenticode e Updates com SHA-256**: valida assinatura digital Win32 (`WinVerifyTrust`) antes de rodar instaladores de dependências, confere o hash **`SHA256SUMS.txt`** no auto-updater e isola downloads em pastas temporárias aleatórias.
- **Proteção contra DLL & PATH Hijacking**: chamadas nativas do sistema usam `DllImportSearchPath.System32` e utilitários do Windows (`powercfg.exe`, `powershell.exe`, `schtasks.exe`, `taskkill.exe`, `explorer.exe`) são executados por caminho absoluto.
- **API Local Opcional Endurecida**: desligada por padrão, escuta apenas em `127.0.0.1`, valida o cabeçalho `Host` (anti-DNS Rebinding), remove CORS aberto (`*`), aceita somente `GET`/`HEAD` e oculta caminhos locais do usuário (`DataDirectory`).
- **Compilação Auditável**: compilado diretamente do código-fonte via GitHub Actions com privilégio mínimo ou `build.ps1`, publicando `SHA256SUMS.txt` em cada release.

### Reportando uma Vulnerabilidade
Caso encontre algum problema de segurança, utilize a aba **Security → Advisories** do GitHub ou entre em contato diretamente com o mantenedor antes da divulgação pública.
