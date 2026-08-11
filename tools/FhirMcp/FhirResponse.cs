// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Microsoft.Health.Fhir.Mcp;

internal sealed record FhirResponse(int StatusCode, Uri RequestUri, JsonElement Resource, string CaptureDirectory);
