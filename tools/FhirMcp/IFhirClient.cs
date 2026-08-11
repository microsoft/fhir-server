// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Mcp;

internal interface IFhirClient
{
    Task EnsureResourceTypeAsync(string resourceType, CancellationToken cancellationToken);

    Task<FhirResponse> GetAsync(
        string operationName,
        IReadOnlyList<string> pathSegments,
        IReadOnlyList<KeyValuePair<string, string>> queryParameters,
        CancellationToken cancellationToken);

    Task<FhirResponse> PostAsync(
        string operationName,
        IReadOnlyList<string> pathSegments,
        string requestBody,
        CancellationToken cancellationToken);
}
