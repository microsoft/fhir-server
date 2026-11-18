#!/usr/bin/env bash
set -euo pipefail

provider="$1"
case "$provider" in
    SqlServer) password="$(sudo cat /run/secrets/sql-password)" ;;
    CosmosDb) ;;
    *) echo "Unsupported dev container database provider." >&2; exit 1 ;;
esac

umask 077
directory="$HOME/.fhir-devcontainer"
mkdir -p "$directory"
chmod 700 "$directory"
for version in R4 STU3 R5; do
    temporary="$(mktemp "$directory/$version.XXXXXX")"
    trap 'rm -f "$temporary"' EXIT
    {
        printf '%s\n' \
            'ASPNETCORE_ENVIRONMENT=Development' \
            'ASPNETCORE_URLS=https://localhost:44348' \
            'FhirServer__Security__Enabled=false' \
            'FhirServer__Security__Authentication__Authority=https://localhost' \
            "DataStore=$provider"
        if [[ "$provider" == SqlServer ]]; then
            printf '%s\n' \
                "SqlServer__ConnectionString=Server=sql,1433;Initial Catalog=FHIR_$version;User ID=sa;Password=$password;Encrypt=True;TrustServerCertificate=True;Connection Timeout=5" \
                'SqlServer__AllowDatabaseCreation=true' \
                'SqlServer__Initialize=true' \
                'SqlServer__SchemaOptions__AutomaticUpdatesEnabled=true'
        else
            printf '%s\n' \
                'CosmosDb__Host=https://localhost:8081/' \
                'CosmosDb__Key=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==' \
                "CosmosDb__DatabaseId=FHIR_$version"
        fi
    } > "$temporary"
    mv "$temporary" "$directory/$version.env"
done

/usr/share/dotnet/dotnet dev-certs https
echo "Private $provider debugger environments and the ASP.NET development certificate are ready."
