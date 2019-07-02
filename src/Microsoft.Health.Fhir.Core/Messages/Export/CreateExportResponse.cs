// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Messages.Export
{
    public class CreateExportResponse
    {
        public string JobId { get; private set; }

        public bool Successful { get; private set; }

        public string FailureReason { get; private set; }

        public static CreateExportResponse Failed(string failureReason)
        {
            EnsureArg.IsNotNullOrWhiteSpace(failureReason, nameof(failureReason));

            return new CreateExportResponse
            {
                Successful = false,
                FailureReason = failureReason,
            };
        }

        public static CreateExportResponse Succeeded(string jobId)
        {
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            return new CreateExportResponse
            {
                Successful = true,
                JobId = jobId,
            };
        }
    }
}
