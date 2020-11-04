// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public interface IFhirRequestContext : IRequestContext
    {
        string ResourceType { get; set; }

        IReadOnlyList<Tuple<string, string>> QueryParameters { get; }

        /// <summary>
        /// A list of issues that will be returned inside a resulting search bundle
        /// </summary>
        IList<OperationOutcomeIssue> BundleIssues { get; }

        bool IncludePartiallyIndexedSearchParams { get; set; }
    }
}
