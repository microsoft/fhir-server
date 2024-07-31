#!/bin/sh
set -e
#systemctl start ssh
/usr/sbin/sshd -D
exec dotnet Microsoft.Health.Fhir.Web.dll "$@"