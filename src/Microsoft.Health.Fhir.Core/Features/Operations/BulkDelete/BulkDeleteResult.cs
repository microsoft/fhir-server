﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete
{
    public class BulkDeleteResult
    {
        [JsonConstructor]
        public BulkDeleteResult()
        {
            ResourcesDeleted = new Dictionary<string, long>();
            Issues = new List<string>();
        }

        [JsonProperty(JobRecordProperties.ResourcesDeleted)]
        public IDictionary<string, long> ResourcesDeleted { get; }

        [JsonProperty(JobRecordProperties.Issues)]
        public ICollection<string> Issues { get; }
    }
}
