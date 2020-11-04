// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Context
{
    public class DefaultFhirRequestContext : IFhirRequestContext
    {
        public string Method { get; set; }

        public Uri BaseUri { get; set; }

        public Uri Uri { get; set; }

        public string CorrelationId { get; set; }

        public string RouteName { get; set; }

        public string AuditEventType { get; set; }

        public ClaimsPrincipal Principal { get; set; }

        public IDictionary<string, StringValues> RequestHeaders { get; set; }

        public IDictionary<string, StringValues> ResponseHeaders { get; set; }

        public IReadOnlyList<Tuple<string, string>> QueryParameters { get; }

        public IList<OperationOutcomeIssue> BundleIssues { get; set; } = new List<OperationOutcomeIssue>();

        public string ResourceType { get; set; }

        public bool IncludePartiallyIndexedSearchParams { get; set; }
    }
}
