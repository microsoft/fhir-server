#!/usr/bin/env bash
set -euo pipefail

SQLCMDPASSWORD="$(sudo cat /run/secrets/sql-password)"
export SQLCMDPASSWORD
error="$(mktemp)"
trap 'rm -f "$error"' EXIT
deadline=$((SECONDS + 90))

echo "Waiting up to 90 seconds for authenticated SQL Server readiness at sql:1433."
until /opt/mssql-tools18/bin/sqlcmd -S sql -U sa -C -l 3 -t 3 -r 1 -b \
    -Q 'SET NOCOUNT ON; SELECT 1' -o /dev/null 2>"$error"; do
    if (( SECONDS >= deadline )); then
        cat "$error" >&2
        echo "SQL Server is not ready. Check the sql service logs for the Compose project in the Dev Containers startup log. Restore the original .devcontainer/.local/sql-password if authentication fails, then restart the dev container." >&2
        exit 1
    fi
    sleep 2
done
unset SQLCMDPASSWORD

/setup/configure-debug.sh SqlServer
echo "SQL Server is ready. Start a dev container debug profile to initialize its FHIR database."
