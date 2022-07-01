// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Import
{
    public class GetImportRequest : IRequest<GetImportResponse>
    {
        public GetImportRequest(long jobId)
        {
            JobId = jobId;
        }

        /// <summary>
        /// Import task id
        /// </summary>
        public long JobId { get; }
    }
}
