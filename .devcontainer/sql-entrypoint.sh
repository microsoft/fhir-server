#!/usr/bin/env bash
set -euo pipefail

secret=/run/secrets/sql-password
if [[ ! -s "$secret" ]]; then
    echo "SQL credential is missing. Restore .devcontainer/.local/sql-password and restart this Compose project." >&2
    exit 1
fi

fingerprint="$(sha256sum "$secret" | cut -d ' ' -f 1)"
stamp=/var/opt/mssql/.devcontainer-password-sha256
if [[ -f "$stamp" ]]; then
    if [[ "$(cat "$stamp")" != "$fingerprint" ]]; then
        echo "SQL credential does not match this data volume. Restore the original .devcontainer/.local/sql-password. The existing database password has not been changed." >&2
        exit 1
    fi
elif [[ -f /var/opt/mssql/data/master.mdf ]]; then
    echo "This SQL volume was not initialized by this dev container. Use a new Compose project or recover the original volume and credential together." >&2
    exit 1
else
    umask 077
    printf '%s' "$fingerprint" > "$stamp"
fi

MSSQL_SA_PASSWORD="$(cat "$secret")"
export MSSQL_SA_PASSWORD
# Compose bind-backed secrets keep host ownership, so read as root before dropping privileges.
exec setpriv --reuid=mssql --regid=mssql --init-groups \
    /opt/mssql/bin/launch_sqlservr.sh "$@"
