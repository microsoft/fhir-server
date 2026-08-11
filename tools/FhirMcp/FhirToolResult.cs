// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Microsoft.Health.Fhir.Mcp;

internal sealed record FhirToolResult(int StatusCode, string RequestUrl, JsonElement Resource, IReadOnlyList<string> CaptureDirectories)
{
    internal static FhirToolResult FromResponse(FhirResponse response) =>
        new(response.StatusCode, response.RequestUri.AbsoluteUri, response.Resource, new[] { response.CaptureDirectory });
}
