#!/bin/sh
set -e
#systemctl start ssh
echo "Starting SSH"
#/usr/sbin/sshd -D\
/usr/sbin/sshd

echo "running test sql command"
sqlcmd -S sergey-perf.database.windows.net -d FHIR4 --authentication-method ActiveDirectoryManagedIdentity -U ce998957-2e67-457b-a1d1-9734919f4a85 -Q "SELECT COUNT(*) FROM dbo.Resource"

echo "Starting FHIR Server"
exec dotnet /app/Microsoft.Health.Fhir.Web.dll
echo "FHIR Server Started"