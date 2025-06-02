// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes.Models.Converters;

namespace Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes.Models;

[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class MetaJsonNode : IExtensionData
{
    [JsonPropertyName("versionId")]
    public string VersionId { get; set; }

    [JsonConverter(typeof(FhirDateTimeConverter))]
    [JsonPropertyName("lastUpdated")]
    public DateTimeOffset? LastUpdated { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; }
}
