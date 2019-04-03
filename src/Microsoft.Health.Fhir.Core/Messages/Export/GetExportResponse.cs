// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Core.Messages.Export
{
    public class GetExportResponse
    {
        public GetExportResponse(bool jobExists)
            : this(jobExists, OperationStatus.Unknown)
        {
        }

        public GetExportResponse(bool jobExists, OperationStatus jobStatus)
        {
            JobExists = jobExists;
            JobStatus = jobStatus;
        }

        public bool JobExists { get; }

        public OperationStatus JobStatus { get; }
    }
}
