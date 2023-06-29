// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete
{
    public class BulkDeleteResult
    {
        [JsonConstructor]
        public BulkDeleteResult()
        {
            ResourcesDeleted = new Dictionary<string, long>();
            Issues = new Collection<string>();
        }

        [JsonProperty(JobRecordProperties.ResourcesDeleted)]
        public Dictionary<string, long> ResourcesDeleted { get; private set; }

        [JsonProperty(JobRecordProperties.Issues)]
        public Collection<string> Issues { get; private set; }
    }
}
