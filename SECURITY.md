# Security Policy / Política de Segurança

## English

### Why does VoltPulse require Administrator privileges?
VoltPulse reads low-level hardware telemetry (CPU RAPL energy counters, motherboard SuperIO voltage/fan sensors, and physical NVMe/SATA S.M.A.R.T. registers). Windows restricts access to these hardware interfaces to elevated processes and signed kernel drivers (such as **PawnIO**).

### Privacy Guarantees
- **No external telemetry or analytics**: VoltPulse never sends hardware or usage data anywhere.
- **No open network ports**: The WebView2 dashboard loads strictly from a local in-memory virtual host (`voltpulse.local`).
- **Auditable builds**: All releases can be built directly from source using GitHub Actions (`.github/workflows/release.yml`) or `build.ps1`.

### Reporting a Vulnerability
If you discover a security issue, please open a private security advisory on GitHub or contact the repository maintainer before public disclosure.

---

## Português (Brasil)

### Por que o VoltPulse precisa de permissão de Administrador?
O VoltPulse lê telemetria de hardware em baixo nível (contadores de energia RAPL da CPU, sensores SuperIO de voltagem/ventoinhas da placa-mãe e registradores S.M.A.R.T. físicos dos SSDs/HDs). O Windows restringe esse acesso a processos elevados e drivers assinados (como o **PawnIO**).

### Garantias de Privacidade
- **Zero telemetria externa**: nenhum dado de hardware ou consumo sai do seu PC.
- **Zero portas de rede abertas**: a interface WebView2 carrega de um host virtual local em memória (`voltpulse.local`).
- **Compilação auditável**: todas as releases são compiladas diretamente do código-fonte via GitHub Actions ou `build.ps1`.

### Reportando uma Vulnerabilidade
Caso encontre algum problema de segurança, utilize a aba **Security → Advisories** do GitHub ou entre em contato diretamente com o mantenedor antes da divulgação pública.
