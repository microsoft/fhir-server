// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Delete;

public class ConditionalDeleteJobResult
{
    [JsonProperty("totalItemsDeleted")]
    public long TotalItemsDeleted { get; set; }

    [JsonProperty("ct")]
    public string ContinuationToken { get; set; }
}
