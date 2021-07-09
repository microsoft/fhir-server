// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Reindex
{
    public class CreateReindexResponse
    {
        public CreateReindexResponse(ReindexJobWrapper job)
        {
            EnsureArg.IsNotNull(job, nameof(job));

            Job = job;
        }

        public ReindexJobWrapper Job { get; }
    }
}
