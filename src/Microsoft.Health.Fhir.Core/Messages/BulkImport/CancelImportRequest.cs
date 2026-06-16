// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;

namespace Microsoft.Health.Fhir.Core.Messages.Import
{
    public class CancelImportRequest : IRequest<CancelImportResponse>
    {
        public CancelImportRequest(long jobId)
        {
            JobId = jobId;
        }

        /// <summary>
        /// Import orchestrator/coordinator job id this is also known as Group Id
        /// </summary>
        public long JobId { get; }
    }
}
