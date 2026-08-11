# FHIR MCP Server

This project exposes a small read-only Model Context Protocol server over stdio. It lets a VS Code agent build its own authenticated FHIR searches at runtime instead of selecting prepared REST Client requests.

## Build

```powershell
dotnet build .\tools\FhirMcp\FhirMcp.csproj -p:DotNetSdkPackageVersion=9.0.18
```

The checked-in `.vscode/mcp.json` launches the `net9.0` Debug assembly, so build once before starting or restarting the `fhir` MCP server in VS Code.

## Authentication

Set credentials in the environment inherited by VS Code. Do not add them to `.vscode/mcp.json`, source files, or tool arguments.

| Variable | Purpose |
|---|---|
| `FHIR_MCP_BASE_URL` | Required absolute FHIR service URL. |
| `FHIR_MCP_BEARER_TOKEN` | Optional fixed bearer token; takes precedence over client credentials. |
| `FHIR_MCP_TOKEN_URL` | OAuth token endpoint required with client credentials. |
| `FHIR_MCP_CLIENT_ID` | OAuth client id. |
| `FHIR_MCP_CLIENT_SECRET` | OAuth client secret. |
| `FHIR_MCP_SCOPE` | OAuth scope; defaults to `fhir-api`. |
| `FHIR_MCP_ALLOW_INSECURE_LOCALHOST` | Allows an invalid certificate only for a loopback request. Defaults to `false`. |
| `FHIR_MCP_CAPTURE_ROOT` | Capture directory; defaults to `%TEMP%\fhir-mcp-captures`. |
| `FHIR_MCP_MAX_COUNT` | Maximum tool result count; defaults to `100`. |
| `FHIR_MCP_MAX_RESPONSE_BYTES` | Maximum response size; defaults to 16 MiB. |

When neither bearer nor client credentials are configured, the client sends an unauthenticated request. This supports FHIR servers that intentionally allow anonymous reads.

## Tools

- `patientSemanticSearch` builds and posts the patient `$semantic-search` FHIR `Parameters` body.
- `searchFhirResources` runs normal or hybrid FHIR searches with encoded, repeatable filter values.
- `readFhirResource` reads a current resource or an exact history version.
- `discoverVectorSearchParameters` finds posted vector SearchParameters and resolves their activation status.

Resource types are validated against the live CapabilityStatement. Search parameter names are syntax-validated but remain server-driven so newly posted custom SearchParameters work without recompiling this project.

Every FHIR call writes `request.json`, `result.json`, and `response.fhir.json` to its capture directory. Captures omit authorization data but can contain clinical data and query text; keep them outside source control and apply appropriate retention controls.

## Tests

```powershell
dotnet test .\src\Microsoft.Health.Fhir.Mcp.UnitTests\Microsoft.Health.Fhir.Mcp.UnitTests.csproj -p:DotNetSdkPackageVersion=9.0.18
```

The implementation uses the [official Model Context Protocol C# SDK](https://csharp.sdk.modelcontextprotocol.io/).