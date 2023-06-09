// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexOrchestratorJobResult
    {
        /// <summary>
        /// Succeeded Reindex resource count
        /// </summary>
        public long SucceededResources { get; set; }

        /// <summary>
        /// Failed processing resource count
        /// </summary>
        public long FailedResources { get; set; }

        /// <summary>
        /// Critical error during data processing.
        /// </summary>
        public string ErrorDetails { get; set; }

        /// <summary>
        /// Count of jobs created for all query processing jobs
        /// </summary>
        public int CreatedJobs { get; set; }

        /// <summary>
        /// Count of completed query processing jobs
        /// </summary>
        public int CompletedJobs { get; set; }

        [JsonProperty(JobRecordProperties.Error)]
        public ICollection<OperationOutcomeIssue> Error { get; private set; } = new List<OperationOutcomeIssue>();
    }
}
