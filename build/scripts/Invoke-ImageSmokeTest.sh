#!/usr/bin/env bash
# Smoke test of a locally built FHIR server image (see build/templates/jobs/tier1-image-smoke.yml).
# Runs the image against the SQL Server and Azurite containers already listening on 127.0.0.1 and asserts:
#   1. with the development identity provider enabled, a token from /connect/token opens GET /metadata and GET /Patient
#      (200), and GET /Patient without a token is 401;
#   2. with DevelopmentIdentityProvider__Enabled=false (a new container, same database), GET /Patient is 401.
# The server uses the host network so 127.0.0.1 reaches the dependencies, and serves HTTPS on 127.0.0.1:8443 with a
# throwaway self-signed certificate, because the JWT bearer handler only accepts an https Authority. The container trusts
# that certificate through SSL_CERT_FILE and curl through --cacert, so nothing is run insecurely.
# Environment:
#   IMAGE                   image to run
#   SQL_CONNECTION_STRING   connection string to master of the SQL Server container (secret; never echoed)
#   REPO_ROOT               repository root (for testauthenvironment.json)
#   WORK_DIR                scratch directory
#   JOB_ID                  value of the fhir-test-dependency label, so the job's cleanup step removes the containers
set -euo pipefail

: "${IMAGE:?}" "${SQL_CONNECTION_STRING:?}" "${REPO_ROOT:?}" "${WORK_DIR:?}" "${JOB_ID:?}"

readonly BASE_URL='https://127.0.0.1:8443'
readonly MOUNT_DIR="$WORK_DIR/mount"
readonly CERT="$MOUNT_DIR/cert.pem"
readonly DATABASE='FHIR_R4_Smoke'
readonly SERVER_NAME="fhir-smoke-$JOB_ID"

dump_logs_on_failure() {
  local code=$?
  if [ "$code" -ne 0 ]; then
    echo "##[group]FHIR server container log"
    docker logs --tail 300 "$SERVER_NAME" 2>&1 || true
    echo "##[endgroup]"
  fi
}
trap dump_logs_on_failure EXIT

fail() {
  echo "##vso[task.logissue type=error]$*"
  exit 1
}

expect_status() {
  local description=$1 expected=$2 actual=$3
  if [ "$actual" != "$expected" ]; then
    fail "$description: expected HTTP $expected, got $actual"
  fi

  echo "PASS: $description (HTTP $actual)"
}

http_status() {
  curl -sS --cacert "$CERT" -o /dev/null -w '%{http_code}' "$@"
}

# $1 = true|false: whether the development identity provider is enabled.
start_server() {
  local identity_provider=$1
  local args=(
    run -d --name "$SERVER_NAME" --label "fhir-test-dependency=$JOB_ID" --network host
    -v "$MOUNT_DIR:/smoke:ro"
    -e ASPNETCORE_URLS="$BASE_URL"
    -e Kestrel__Certificates__Default__Path=/smoke/cert.pem
    -e Kestrel__Certificates__Default__KeyPath=/smoke/key.pem
    -e SSL_CERT_FILE=/smoke/cert.pem
    # OpenIddict keeps its development certificates in the user's certificate store, which needs a writable home.
    -e HOME=/tmp
    -e DataStore=SqlServer
    -e SqlServer__ConnectionString
    -e SqlServer__AllowDatabaseCreation=true
    -e SqlServer__Initialize=true
    -e SqlServer__SchemaOptions__AutomaticUpdatesEnabled=true
    -e FhirServer__Security__Enabled=true
    -e FhirServer__Security__Authentication__Authority="$BASE_URL"
    -e FhirServer__Security__Authentication__Audience=fhir-api
    -e FhirServer__Operations__Export__StorageAccountConnection=UseDevelopmentStorage=true
    -e FhirServer__Operations__IntegrationDataStore__StorageAccountConnection=UseDevelopmentStorage=true
    -e DevelopmentIdentityProvider__Enabled="$identity_provider"
  )

  # testauthenvironment.json switches the identity provider on whatever DevelopmentIdentityProvider__Enabled says, so the
  # run without a provider must not see it.
  if [ "$identity_provider" = true ]; then
    args+=(-e TestAuthEnvironment__FilePath=/smoke/testauthenvironment.json)
  fi

  echo "Starting $SERVER_NAME (development identity provider: $identity_provider)"
  docker "${args[@]}" "$IMAGE" > /dev/null
}

wait_until_ready() {
  # /metadata is anonymous; it answers 200 once the schema is initialized and the host is serving.
  if ! curl -fsS --cacert "$CERT" --retry 40 --retry-delay 5 --retry-connrefused --retry-all-errors --retry-max-time 300 \
    -o /dev/null "$BASE_URL/metadata"; then
    fail "The FHIR server did not answer GET /metadata within 5 minutes"
  fi
}

mkdir -p "$MOUNT_DIR"
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj '/CN=localhost' \
  -addext 'subjectAltName=DNS:localhost,IP:127.0.0.1' \
  -keyout "$MOUNT_DIR/key.pem" -out "$CERT" 2> /dev/null
cp "$REPO_ROOT/testauthenvironment.json" "$MOUNT_DIR/testauthenvironment.json"
# The server runs as the non-root user of the image; the files are a throwaway certificate and public test clients.
chmod 755 "$MOUNT_DIR"
chmod 644 "$MOUNT_DIR"/*

export SqlServer__ConnectionString="${SQL_CONNECTION_STRING/Initial Catalog=master/Initial Catalog=$DATABASE}"
if [ "$SqlServer__ConnectionString" = "$SQL_CONNECTION_STRING" ]; then
  fail "The SQL connection string has no 'Initial Catalog=master' to replace"
fi

echo "=== 1. Development identity provider enabled ==="
start_server true
wait_until_ready

# The development identity provider accepts the client id as the client secret; see testauthenvironment.json.
token_response=$(curl -sS --cacert "$CERT" "$BASE_URL/connect/token" \
  -d grant_type=client_credentials -d client_id=globalAdminServicePrincipal -d client_secret=globalAdminServicePrincipal \
  -d scope=fhir-api)
token=$(printf '%s' "$token_response" | grep -o '"access_token":"[^"]*"' | cut -d'"' -f4 || true)
if [ -z "$token" ]; then
  fail "No access token from /connect/token. Response: $token_response"
fi

expect_status 'authenticated GET /metadata' 200 "$(http_status -H "Authorization: Bearer $token" "$BASE_URL/metadata")"
# /metadata is anonymous, so it proves nothing about the token; a resource read does.
expect_status 'authenticated GET /Patient' 200 "$(http_status -H "Authorization: Bearer $token" "$BASE_URL/Patient")"
expect_status 'unauthenticated GET /Patient (provider enabled)' 401 "$(http_status "$BASE_URL/Patient")"

docker rm -f "$SERVER_NAME" > /dev/null

echo "=== 2. Development identity provider disabled ==="
start_server false
wait_until_ready
expect_status 'unauthenticated GET /Patient (provider disabled)' 401 "$(http_status "$BASE_URL/Patient")"
