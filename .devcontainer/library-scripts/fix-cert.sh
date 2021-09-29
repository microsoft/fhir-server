#!/usr/bin/env bash

# Download and install the emulator cert so the fhir server can communicate with TLS/SSL
# https://docs.microsoft.com/en-us/azure/cosmos-db/linux-emulator?tabs=ssl-netstd21#run-on-macos
curl -k https://cosmos:8081/_explorer/emulator.pem > ~/emulatorcert.crt
sudo cp ~/emulatorcert.crt /usr/local/share/ca-certificates/
sudo update-ca-certificates