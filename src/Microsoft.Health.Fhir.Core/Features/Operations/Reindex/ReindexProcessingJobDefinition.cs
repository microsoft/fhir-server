// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.JobManagement;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexProcessingJobDefinition : IJobData
    {
        public int TypeId { get; set; }

        public long GroupId { get; init; }

        public string ResourceType { get; set; }

        public DateTimeOffset SearchParamLastUpdated { get; set; }

        public string SearchParameterHash { get; set; }

        public SearchResultReindex ResourceCount { get; set; }

        public uint MaximumNumberOfResourcesPerQuery { get; set; }

        public uint MaximumNumberOfResourcesPerWrite { get; set; }

        [JsonConverter(typeof(ReindexSearchParameterUrlStatusesConverter))]
        public IReadOnlyCollection<(string Url, SearchParameterStatus Status)> SearchParameterUrlStatuses { get; set; }
    }
}
