// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models
{
    /// <summary>
    /// Class to hold the count as well as the start and end range Ids for one resource type
    /// </summary>
    public class ReindexJobResourceCount
    {
        public ReindexJobResourceCount(int count, long startResourceSurrogateId, long endResourceSurrogateId)
        {
            StartResourceSurrogateId = EnsureArg.IsNotDefault<long>(startResourceSurrogateId, nameof(startResourceSurrogateId));
            EndResourceSurrogateId = EnsureArg.IsNotDefault<long>(endResourceSurrogateId, nameof(endResourceSurrogateId));
        }

        [JsonConstructor]
        protected ReindexJobResourceCount()
        {
        }

        [JsonProperty(JobRecordProperties.Count)]
        public long Count { get; set; }

        [JsonProperty(JobRecordProperties.StartResourceSurrogateId)]
        public long StartResourceSurrogateId { get; set; }

        [JsonProperty(JobRecordProperties.EndResourceSurrogateId)]
        public long EndResourceSurrogateId { get; set; }
    }
}
