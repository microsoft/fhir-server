// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;

namespace Microsoft.Health.Fhir.Mcp;

internal sealed class FhirMcpOptions
{
    internal const string BaseUrlVariable = "FHIR_MCP_BASE_URL";
    internal const string TokenUrlVariable = "FHIR_MCP_TOKEN_URL";
    internal const string ClientIdVariable = "FHIR_MCP_CLIENT_ID";
    internal const string ClientSecretVariable = "FHIR_MCP_CLIENT_SECRET";
    internal const string BearerTokenVariable = "FHIR_MCP_BEARER_TOKEN";
    internal const string ScopeVariable = "FHIR_MCP_SCOPE";
    internal const string AllowInsecureLocalhostVariable = "FHIR_MCP_ALLOW_INSECURE_LOCALHOST";
    internal const string CaptureRootVariable = "FHIR_MCP_CAPTURE_ROOT";
    internal const string MaxCountVariable = "FHIR_MCP_MAX_COUNT";
    internal const string MaxResponseBytesVariable = "FHIR_MCP_MAX_RESPONSE_BYTES";

    internal FhirMcpOptions(
        Uri baseAddress,
        Uri? tokenAddress,
        string? clientId,
        string? clientSecret,
        string? bearerToken,
        string scope,
        bool allowInsecureLocalhost,
        string captureRoot,
        int maxCount,
        int maxResponseBytes)
    {
        BaseAddress = baseAddress;
        TokenAddress = tokenAddress;
        ClientId = clientId;
        ClientSecret = clientSecret;
        BearerToken = bearerToken;
        Scope = scope;
        AllowInsecureLocalhost = allowInsecureLocalhost;
        CaptureRoot = captureRoot;
        MaxCount = maxCount;
        MaxResponseBytes = maxResponseBytes;
    }

    internal Uri BaseAddress { get; }

    internal Uri? TokenAddress { get; }

    internal string? ClientId { get; }

    internal string? ClientSecret { get; }

    internal string? BearerToken { get; }

    internal string Scope { get; }

    internal bool AllowInsecureLocalhost { get; }

    internal string CaptureRoot { get; }

    internal int MaxCount { get; }

    internal int MaxResponseBytes { get; }

    internal static FhirMcpOptions FromEnvironment()
    {
        string baseUrl = GetRequiredVariable(BaseUrlVariable);
        Uri baseAddress = ParseAbsoluteHttpUri(baseUrl, BaseUrlVariable, ensureTrailingSlash: true);
        string? tokenUrl = GetOptionalVariable(TokenUrlVariable);
        string? clientId = GetOptionalVariable(ClientIdVariable);
        string? clientSecret = GetOptionalVariable(ClientSecretVariable);
        string? bearerToken = GetOptionalVariable(BearerTokenVariable);

        if ((clientId is null) != (clientSecret is null))
        {
            throw new InvalidOperationException($"{ClientIdVariable} and {ClientSecretVariable} must be configured together.");
        }

        if (bearerToken is null && clientId is not null && tokenUrl is null)
        {
            throw new InvalidOperationException($"{TokenUrlVariable} is required when client credentials are configured.");
        }

        Uri? tokenAddress = tokenUrl is null ? null : ParseAbsoluteHttpUri(tokenUrl, TokenUrlVariable, ensureTrailingSlash: false);
        string captureRoot = GetOptionalVariable(CaptureRootVariable) ?? Path.Combine(Path.GetTempPath(), "fhir-mcp-captures");

        return new FhirMcpOptions(
            baseAddress,
            tokenAddress,
            clientId,
            clientSecret,
            bearerToken,
            GetOptionalVariable(ScopeVariable) ?? "fhir-api",
            ParseBoolean(AllowInsecureLocalhostVariable, defaultValue: false),
            Path.GetFullPath(captureRoot),
            ParsePositiveInteger(MaxCountVariable, defaultValue: 100),
            ParsePositiveInteger(MaxResponseBytesVariable, defaultValue: 16 * 1024 * 1024));
    }

    private static string GetRequiredVariable(string name) =>
        GetOptionalVariable(name) ?? throw new InvalidOperationException($"Environment variable {name} is required.");

    private static string? GetOptionalVariable(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool ParseBoolean(string name, bool defaultValue)
    {
        string? value = GetOptionalVariable(name);
        if (value is null)
        {
            return defaultValue;
        }

        return bool.TryParse(value, out bool result)
            ? result
            : throw new InvalidOperationException($"Environment variable {name} must be 'true' or 'false'.");
    }

    private static int ParsePositiveInteger(string name, int defaultValue)
    {
        string? value = GetOptionalVariable(name);
        if (value is null)
        {
            return defaultValue;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result) && result > 0
            ? result
            : throw new InvalidOperationException($"Environment variable {name} must be a positive integer.");
    }

    private static Uri ParseAbsoluteHttpUri(string value, string name, bool ensureTrailingSlash)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException($"Environment variable {name} must be an absolute HTTP(S) URL without a query or fragment.");
        }

        if (!ensureTrailingSlash || uri.AbsolutePath.EndsWith('/'))
        {
            return uri;
        }

        return new UriBuilder(uri) { Path = uri.AbsolutePath + "/" }.Uri;
    }
}
