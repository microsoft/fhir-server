// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes.Models.Converters;

namespace Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes.Models;

[SuppressMessage("Design", "CA2227", Justification = "POCO style model")]
public class MetaJsonNode : IExtensionData
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("versionId")]
    public string VersionId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(FhirDateTimeConverter))]
    [JsonPropertyName("lastUpdated")]
    public DateTimeOffset? LastUpdated { get; set; }

    /// <summary>
    /// Collection of FHIR extensions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("extension")]
    public Collection<ExtensionJsonNode> Extensions { get; set; }

    /// <summary>
    /// Data not mapped to a model property.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; }
}
