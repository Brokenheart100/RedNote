#!/bin/sh
set -eu
# Aspire supplies secrets as separate environment values. jq's standard @uri
# encoder handles generated passwords containing URI delimiters correctly.
if [ -n "${PGHOST:-}" ]; then
    user=$(printf '%s' "$PGUSER" | jq -sRr @uri)
    password=$(printf '%s' "$PGPASSWORD" | jq -sRr @uri)
    export GORSE_DATA_STORE="postgresql://$user:$password@$PGHOST:$PGPORT/gorsedb?sslmode=disable"
fi
# Aspire supplies GORSE_CACHE_STORE as its native URI (redis:// or rediss://).
exec /usr/bin/gorse-in-one -c /etc/gorse/config.toml "$@"
