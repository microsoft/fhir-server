#!/bin/sh
set -e
#systemctl start ssh
/usr/sbin/sshd -D
dotnet Microsoft.Health.Fhir.Web.dll