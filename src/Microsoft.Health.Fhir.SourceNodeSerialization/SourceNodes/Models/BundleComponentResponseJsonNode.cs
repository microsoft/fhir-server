// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes.Models;

public class BundleComponentResponseJsonNode
{
    [JsonPropertyName("lastModified")]
    public string LastModified { get; set; }

    [JsonPropertyName("etag")]
    public string Etag { get; set; }
}
