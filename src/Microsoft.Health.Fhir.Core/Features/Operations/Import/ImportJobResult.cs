// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportJobResult
    {
        /// <summary>
        /// Transaction time for import task created
        /// </summary>
        [JsonProperty("transactionTime")]
        public DateTimeOffset TransactionTime { get; set; }

        /// <summary>
        /// Request Uri for the import opearion
        /// </summary>
        [JsonProperty("request")]
        public string Request { get; set; }

        /// <summary>
        /// Operation output for the success imported resources
        /// </summary>
        [JsonProperty("output")]
        public IReadOnlyCollection<ImportOperationOutcome> Output { get; set; }

        /// <summary>
        /// Operation output for the failed imported resources
        /// </summary>
        [JsonProperty("error")]
        public IReadOnlyCollection<ImportFailedOperationOutcome> Error { get; set; }

        /// <summary>
        /// Performance metrics collected during import processing. Only populated when
        /// IntegrationDataStore:EnableTestSourceOverride is enabled (test-only feature).
        /// Maps job IDs to their wall-clock execution duration in milliseconds.
        /// Later, SQL call durations can be subtracted to isolate CPU-only processing time.
        /// </summary>
        [JsonProperty("executionStats", NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyDictionary<string, long> ExecutionStats { get; set; }
    }
}
