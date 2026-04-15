#!/usr/bin/env bash

set -euo pipefail

# Install the emulator certificate so the .NET SDK can connect over HTTPS.
# https://learn.microsoft.com/azure/cosmos-db/emulator-linux

emulator_host="${COSMOS_EMULATOR_HOST:-localhost}"
emulator_port="${COSMOS_EMULATOR_PORT:-8081}"
emulator_cert_path="${HOME}/cosmos-emulator.crt"

until openssl s_client -connect "${emulator_host}:${emulator_port}" </dev/null 2>/dev/null | sed -ne '/-BEGIN CERTIFICATE-/,/-END CERTIFICATE-/p' > "${emulator_cert_path}" && [ -s "${emulator_cert_path}" ]; do
    printf '.'
    sleep 3
done

echo
sudo cp "${emulator_cert_path}" /usr/local/share/ca-certificates/cosmos-emulator.crt
sudo update-ca-certificates
