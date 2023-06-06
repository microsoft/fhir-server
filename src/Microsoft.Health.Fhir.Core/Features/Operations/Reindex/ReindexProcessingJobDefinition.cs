// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexProcessingJobDefinition : IJobData
    {
        public int TypeId { get; set; }

        public long StartResourceSurrogateId { get; set; }

        public long EndResourceSurrogateId { get; set; }

        public long GroupId { get; set; }
    }
}
