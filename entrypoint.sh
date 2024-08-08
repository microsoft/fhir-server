#!/bin/sh
set -e

echo "Starting SSH"
/usr/sbin/sshd &

# Temporarily disable exit on error
set +e

echo "Running SQL command"
sqlcmd -S sergey-perf.database.windows.net -d FHIR4 --authentication-method ActiveDirectoryManagedIdentity -U ce998957-2e67-457b-a1d1-9734919f4a85 -Q "SELECT COUNT(*) FROM dbo.Resource"
SQLCMD_EXIT_CODE=$?

# Re-enable exit on error
set -e

if [ $SQLCMD_EXIT_CODE -ne 0 ]; then
    echo "sqlcmd failed with exit code $SQLCMD_EXIT_CODE"
else
    echo "sqlcmd succeeded"
fi

echo "Starting FHIR Server"
exec dotnet /app/Microsoft.Health.Fhir.Web.dll
