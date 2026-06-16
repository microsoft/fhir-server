// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Medino;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Core.Messages.Reindex
{
    public class UpdateReindexRequest : IRequest<UpdateReindexResponse>
    {
        public UpdateReindexRequest(string jobId, OperationStatus status)
        {
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            Status = status;
        }

        public OperationStatus Status { get; }
    }
}
