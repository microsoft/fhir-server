// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Microsoft.Health.Fhir.Mcp;

internal sealed record VectorSearchParameterDefinition(
    string? Id,
    string CanonicalUrl,
    string? Version,
    string? Name,
    string? Code,
    IReadOnlyList<string> BaseResourceTypes,
    string? Expression,
    string? DefinitionStatus,
    string? ActivationStatus,
    JsonElement Configuration);
