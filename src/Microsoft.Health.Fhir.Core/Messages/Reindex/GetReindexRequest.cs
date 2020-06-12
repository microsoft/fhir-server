// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Reindex
{
    public class GetReindexRequest : IRequest<GetReindexResponse>
    {
        public GetReindexRequest(string jobId = null)
        {
            JobId = jobId;
        }

        public string JobId { get; }
    }
}
