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

        public IList<OperationOutcomeIssue> BundleIssues { get; set; } = new List<OperationOutcomeIssue>();

        public string ResourceType { get; set; }

        public bool IncludePartiallyIndexedSearchParams { get; set; }

        public bool ExecutingBatchOrTransaction { get; set; }

        public bool IsBackgroundTask { get; set; }

        public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();

        public AccessControlContext AccessControlContext { get; set; } = new AccessControlContext();

        public object Clone()
        {
            KeyValuePair<string, StringValues>[] requestHeaders = new KeyValuePair<string, StringValues>[RequestHeaders.Count];
            RequestHeaders.CopyTo(requestHeaders, 0);

            KeyValuePair<string, StringValues>[] responseHeaders = new KeyValuePair<string, StringValues>[ResponseHeaders.Count];
            ResponseHeaders.CopyTo(responseHeaders, 0);

            var clone = new FhirRequestContext(
                Method,
                Uri.ToString(),
                BaseUri.ToString(),
                CorrelationId,
                requestHeaders: new Dictionary<string, StringValues>(requestHeaders),
                responseHeaders: new Dictionary<string, StringValues>(responseHeaders));

            clone.RouteName = RouteName;
            clone.AuditEventType = AuditEventType;
            clone.Principal = Principal.Clone();
            clone.ResourceType = ResourceType;
            clone.IncludePartiallyIndexedSearchParams = IncludePartiallyIndexedSearchParams;
            clone.ExecutingBatchOrTransaction = ExecutingBatchOrTransaction;
            clone.IsBackgroundTask = IsBackgroundTask;
            clone.AccessControlContext = (AccessControlContext)AccessControlContext.Clone();

            foreach (OperationOutcomeIssue bundleIssue in BundleIssues)
            {
                clone.BundleIssues.Add(bundleIssue);
            }

            foreach (KeyValuePair<string, object> property in Properties)
            {
                clone.Properties.Add(property);
            }

            return clone;
        }
    }
}
