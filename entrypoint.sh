#!/bin/sh
set -e
#systemctl start ssh
echo "Starting SSH"
#/usr/sbin/sshd -D\
/usr/sbin/sshd
echo "Starting FHIR Server"
exec dotnet /app/Microsoft.Health.Fhir.Web.dll
echo "FHIR Server Started"