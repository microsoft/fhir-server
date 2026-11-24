#!/usr/bin/env bash
set -euo pipefail

directory=/devcontainer/.local
password="$directory/sql-password"

if [[ -d "$directory" ]]; then
    if [[ ! -s "$password" ]]; then
        echo "SQL credential is missing. Restore .devcontainer/.local/sql-password from your local backup. Do not generate a new password for an existing SQL volume." >&2
        exit 1
    fi
else
    umask 077
    mkdir "$directory"
    temporary="$(mktemp "$directory/password.XXXXXX")"
    trap 'rm -f "$temporary"' EXIT
    random="$(openssl rand -hex 24)"
    printf 'Fhir1!%s' "$random" > "$temporary"
    mv "$temporary" "$password"
    chown --reference=/devcontainer "$directory" "$password"
    echo "Created the private local SQL credential in .devcontainer/.local/sql-password. Keep it with the SQL data volume."
fi

if ! grep -Eq '^Fhir1![0-9a-f]{48}$' "$password"; then
    echo "SQL credential has an unexpected format. Restore the original generated .devcontainer/.local/sql-password without changing its contents." >&2
    exit 1
fi
