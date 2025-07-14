// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models
{
    /// <summary>
    /// Represents a signal to notify a reindex job about new search parameters
    /// </summary>
    public class ReindexJobUpdateSignal
    {
        /// <summary>
        /// The ID of the reindex job that should be notified
        /// </summary>
        [JsonProperty("targetJobId")]
        public string TargetJobId { get; set; }

        /// <summary>
        /// When this signal was created
        /// </summary>
        [JsonProperty("timestamp")]
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// The type of signal (e.g., "SearchParameterUpdate")
        /// </summary>
        [JsonProperty("signalType")]
        public string SignalType { get; set; }
    }
}
