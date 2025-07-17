﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexProcessingJobDefinition : IJobData
    {
        public int TypeId { get; set; }

        public long GroupId { get; internal set; }

        public string ResourceType { get; set; }

        public string ResourceTypeSearchParameterHashMap { get; set; }

        public SearchResultReindex ResourceCount { get; set; }

        public uint MaximumNumberOfResourcesPerQuery { get; set; }

        public uint MaximumNumberOfResourcesPerWrite { get; set; }

        public IReadOnlyCollection<string> SearchParameterUrls { get; set; }
    }
}
