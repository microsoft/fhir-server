// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete
{
    public class BulkDeleteResult
    {
        public BulkDeleteResult()
        {
            ResourcesDeleted = new Dictionary<string, long>();
            Issues = new List<OperationOutcomeIssue>();
        }

        [JsonConstructor]
        protected BulkDeleteDescription()
        {
        }

        public Dictionary<string, long> ResourcesDeleted { get; private set; }

        public IList<OperationOutcomeIssue> Issues { get; private set; }
    }
}
