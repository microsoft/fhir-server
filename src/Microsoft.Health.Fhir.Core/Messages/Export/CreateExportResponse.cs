// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Core.Messages.Export
{
    public class CreateExportResponse
    {
        public CreateExportResponse(string id, JobCreationStatus jobCreationStatus)
        {
            EnsureArg.IsNotNullOrEmpty(id, nameof(id));

            Id = id;
            JobStatus = jobCreationStatus;
        }

        public string Id { get; }

        public JobCreationStatus JobStatus { get; }
    }
}
