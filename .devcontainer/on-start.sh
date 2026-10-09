#!/usr/bin/env bash
set -euo pipefail
task_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$task_root"

# Also runs after a stopped Codespace resumes; preserve native certificate trust.
if [[ "${REDNOTE_CODESPACES_AUTOSTART:-true}" == "true" ]]; then
    bash scripts/start-codespaces.sh
else
    aspire certs trust --non-interactive
    printf 'Automatic startup disabled. Start: bash scripts/start-codespaces.sh\n'
fi
