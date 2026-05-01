// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.Models
{
    /// <summary>
    /// Represents a surrogate ID sub-range assigned to an export processing job.
    /// </summary>
    public class ExportSurrogateIdRange
    {
        [JsonConstructor]
        public ExportSurrogateIdRange(string startId, string endId)
        {
            StartId = startId;
            EndId = endId;
        }

        [JsonProperty("startId")]
        public string StartId { get; }

        [JsonProperty("endId")]
        public string EndId { get; }
    }
}
