#!/usr/bin/env bash
set -euo pipefail

endpoint="https://localhost:8081/_explorer/emulator.pem"
certificate="$(mktemp)"
error="$(mktemp)"
trap 'rm -f "$certificate" "$error"' EXIT
deadline=$((SECONDS + 300))

echo "Waiting up to five minutes for the Cosmos DB emulator certificate at $endpoint."
# The emulator's self-signed certificate is not trusted until this bootstrap completes.
until curl --insecure --fail --silent --show-error --connect-timeout 3 --max-time 5 \
    "$endpoint" --output "$certificate" 2>"$error"; do
    if (( SECONDS >= deadline )); then
        cat "$error" >&2
        echo "Cosmos DB emulator did not become ready. Check the Cosmos container logs for the Compose project shown in the Dev Containers startup log, then restart the dev container." >&2
        exit 1
    fi
    sleep 2
done

openssl x509 -in "$certificate" -noout -checkend 0
sudo install -m 0644 "$certificate" /usr/local/share/ca-certificates/cosmos-emulator.crt
sudo update-ca-certificates

curl --fail --silent --show-error --connect-timeout 3 --max-time 10 "$endpoint" --output /dev/null
/setup/configure-debug.sh CosmosDb
echo "Cosmos DB HTTPS trust and the ASP.NET development certificate are ready."
