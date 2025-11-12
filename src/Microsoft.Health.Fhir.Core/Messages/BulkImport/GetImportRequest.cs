// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;

namespace Microsoft.Health.Fhir.Core.Messages.Import
{
    public class GetImportRequest : IRequest<GetImportResponse>
    {
        public GetImportRequest(long jobId, bool returnDetails = false)
        {
            JobId = jobId;
            ReturnDetails = returnDetails;
        }

        /// <summary>
        /// Import task id
        /// </summary>
        public long JobId { get; }

        /// <summary>
        /// If true, outcomes are returned of processing job level
        /// </summary>
        public bool ReturnDetails { get; }
    }
}
