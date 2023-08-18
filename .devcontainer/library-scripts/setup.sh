#!/usr/bin/env bash

# Download and install the emulator cert so the fhir server can communicate with TLS/SSL
# https://docs.microsoft.com/en-us/azure/cosmos-db/linux-emulator?tabs=ssl-netstd21#run-on-macos

HTTPD="0"
until [ "$HTTPD" == "200" ]; do
    printf '.'
    sleep 3
    HTTPD=`curl -A "Web Check" -sLk --connect-timeout 3 -w "%{http_code}\n" "https://localhost:8081/_explorer/emulator.pem" -o /dev/null`
done

curl -k https://localhost:8081/_explorer/emulator.pem > ~/emulatorcert.crt
sudo cp ~/emulatorcert.crt /usr/local/share/ca-certificates/
sudo update-ca-certificates
