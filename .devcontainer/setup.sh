#!/usr/bin/env bash
set -euo pipefail
task_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$task_root"
mkdir -p "${HOME}/.aspnet/dev-certs/trust"

dotnet restore RedNote.slnx
pwsh -NoProfile -File scripts/initialize-admin-secrets.ps1
for task_app in RedNote.Bff.Shared Red-Book RedNote.Admin; do
    (cd "$task_app" && npm ci)
done
printf '\nRedNote dependencies are ready. Start: bash scripts/start-codespaces.sh\n'
