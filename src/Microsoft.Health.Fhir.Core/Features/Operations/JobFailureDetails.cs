// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public class JobFailureDetails
    {
        public JobFailureDetails(string failureReason, HttpStatusCode statusCode, string failureDetails = "", bool isCustomerCaused = false)
        {
            EnsureArg.IsNotNullOrWhiteSpace(failureReason, nameof(failureReason));

            FailureReason = failureReason;
            FailureStatusCode = statusCode;
            FailureDetails = failureDetails;
            IsCustomerCaused = isCustomerCaused;
        }

        [JsonConstructor]
        private JobFailureDetails()
        {
        }

        [JsonProperty(JobRecordProperties.FailureReason)]
        public string FailureReason { get; private set; }

        [JsonProperty(JobRecordProperties.FailureDetails)]
        public string FailureDetails { get; private set; }

        [JsonProperty(JobRecordProperties.FailureStatusCode)]
        public HttpStatusCode FailureStatusCode { get; private set; }

        /// <summary>
        /// Gets whether the failure was caused by customer configuration or input.
        /// </summary>
        [JsonProperty(JobRecordProperties.IsCustomerCaused, DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsCustomerCaused { get; private set; }
    }
}
