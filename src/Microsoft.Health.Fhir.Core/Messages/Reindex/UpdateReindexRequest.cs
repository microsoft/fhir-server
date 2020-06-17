// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Core.Messages.Reindex
{
    public class UpdateReindexRequest : IRequest<UpdateReindexResponse>
    {
        public UpdateReindexRequest(string jobId, OperationStatus status, int? maximumConcurrency = null)
        {
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            MaximumConcurrency = maximumConcurrency;
            Status = status;
        }

        public int? MaximumConcurrency { get; }

        public OperationStatus Status { get; }
    }
}
