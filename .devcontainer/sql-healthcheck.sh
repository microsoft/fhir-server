#!/usr/bin/env bash
set -euo pipefail

SQLCMDPASSWORD="$(cat /run/secrets/sql-password)"
export SQLCMDPASSWORD
exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -l 3 -t 3 -r 1 -b -Q 'SET NOCOUNT ON; SELECT 1' -o /dev/null
