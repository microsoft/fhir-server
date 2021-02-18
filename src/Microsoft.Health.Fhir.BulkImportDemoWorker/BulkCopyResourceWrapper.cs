// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class BulkCopyResourceWrapper
    {
        public BulkCopyResourceWrapper(ResourceElement resource, long surrogateId)
        {
            Resource = resource;
            SurrogateId = surrogateId;
        }

        public ResourceElement Resource { get; set; }

        public long SurrogateId { get; set; }
    }
}
