// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class BulkCopySearchParamWrapper
    {
        public BulkCopySearchParamWrapper(
            ResourceElement resource,
            SearchIndexEntry searchIndexEntry,
            long surrogateId)
        {
            Resource = resource;
            SurrogateId = surrogateId;
            SearchIndexEntry = searchIndexEntry;
        }

        public ResourceElement Resource { get; set; }

        public SearchIndexEntry SearchIndexEntry { get; set; }

        public long SurrogateId { get; set; }
    }
}
