// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Messages
{
    public class CancelBulkUpdateRequest : IRequest<CancelBulkUpdateResponse>
    {
        public CancelBulkUpdateRequest(long jobId)
        {
            JobId = jobId;
        }

        public long JobId { get; }
    }
}
