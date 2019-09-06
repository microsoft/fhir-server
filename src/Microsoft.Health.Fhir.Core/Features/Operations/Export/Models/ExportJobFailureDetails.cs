// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.Models
{
    public class ExportJobFailureDetails
    {
        public ExportJobFailureDetails(string failureReason, HttpStatusCode statusCode)
        {
            EnsureArg.IsNotNullOrWhiteSpace(failureReason, nameof(failureReason));

            FailureReason = failureReason;
            FailureStatusCode = statusCode;
        }

        [JsonConstructor]
        private ExportJobFailureDetails()
        {
        }

        [JsonProperty(JobRecordProperties.FailureReason)]
        public string FailureReason { get; }

        [JsonProperty(JobRecordProperties.FailureStatusCode)]
        public HttpStatusCode FailureStatusCode { get; }
    }
}
