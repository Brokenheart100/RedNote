#!/usr/bin/env bash
set -euo pipefail
task_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$task_root"

docker info >/dev/null
aspire certs trust --non-interactive
export SSL_CERT_DIR="${HOME}/.aspnet/dev-certs/trust"
export SSL_CERT_FILE=/etc/ssl/certs/ca-certificates.crt
export NODE_USE_SYSTEM_CA=1
export ASPIRE_DASHBOARD_FORWARDEDHEADERS_ENABLED=true
pwsh -NoProfile -File scripts/initialize-admin-secrets.ps1

# Native background lifecycle; logs/status/stop remain available through Aspire CLI.
aspire start --apphost RedNote.AppHost/RedNote.AppHost.csproj --non-interactive
printf '\nUse Ports → 8443 for RedNote; append /admin/ for administration.\n'
printf 'Use Ports → 17286 for the dashboard; keep its Aspire login token.\n'
printf 'Resource status: aspire ps\nStop: aspire stop\n'
