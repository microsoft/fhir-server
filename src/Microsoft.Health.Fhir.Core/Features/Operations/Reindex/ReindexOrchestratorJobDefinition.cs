// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Security.Policy;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexOrchestratorJobDefinition : IJobData
    {
        public int TypeId { get; set; }

        public uint MaximumNumberOfResourcesPerQuery { get; set; }

        public uint MaximumNumberOfResourcesPerWrite { get; set; }

        public IReadOnlyDictionary<string, string> ResourceTypeSearchParameterHashMap { get; set; }

        public string Id { get; internal set; }
    }
}
