// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Mcp;

internal interface IFhirCaptureWriter
{
    Task<string> CaptureAsync(
        string operationName,
        HttpMethod method,
        Uri requestUri,
        string? requestBody,
        HttpResponseMessage? response,
        string? responseBody,
        TimeSpan elapsed,
        string? error,
        CancellationToken cancellationToken);
}
