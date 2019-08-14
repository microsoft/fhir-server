// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Messages.Export
{
    public class CreateExportResponse
    {
        public string JobId { get; private set; }

        public bool Successful
        {
            get { return JobId != null; }
        }

        public string FailureReason { get; private set; }

        public HttpStatusCode FailureStatusCode { get; private set; }

        public static CreateExportResponse Failed(string failureReason, HttpStatusCode statusCode)
        {
            EnsureArg.IsNotNullOrWhiteSpace(failureReason, nameof(failureReason));

            return new CreateExportResponse
            {
                FailureReason = failureReason,
                FailureStatusCode = statusCode,
            };
        }

        public static CreateExportResponse Succeeded(string jobId)
        {
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            return new CreateExportResponse
            {
                JobId = jobId,
            };
        }
    }
}
