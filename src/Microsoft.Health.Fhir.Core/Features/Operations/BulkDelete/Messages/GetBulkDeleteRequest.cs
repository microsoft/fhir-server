// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Messages
{
    public class GetBulkDeleteRequest : IRequest<GetBulkDeleteResponse>
    {
        public GetBulkDeleteRequest(long jobId)
        {
            JobId = jobId;
        }

        public long JobId { get; }
    }
}
