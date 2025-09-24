// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Partitioning;
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

        public int? LogicalPartitionId { get; set; }

        public string PartitionName { get; set; }

        public int GetEffectivePartitionId(string resourceType = null)
        {
            const int SystemPartitionId = 1;
            const int DefaultPartitionId = 2;

            // If partitioning is disabled, use default partition
            if (LogicalPartitionId == null)
            {
                return DefaultPartitionId;
            }

            // System resources always use system partition
            if (!string.IsNullOrEmpty(resourceType) && SystemResourceTypes.IsSystemResource(resourceType))
            {
                return SystemPartitionId;
            }

            // Regular resources use current partition context
            return LogicalPartitionId.Value;
        }

        public object Clone()
        {
            KeyValuePair<string, StringValues>[] requestHeaders = new KeyValuePair<string, StringValues>[RequestHeaders.Count];
            RequestHeaders.CopyTo(requestHeaders, 0);

            KeyValuePair<string, StringValues>[] responseHeaders = new KeyValuePair<string, StringValues>[ResponseHeaders.Count];
            ResponseHeaders.CopyTo(responseHeaders, 0);

            var clone = new DefaultFhirRequestContext();
            clone.Method = Method ?? "GET";
            clone.Uri = Uri ?? new Uri("https://localhost/");
            clone.BaseUri = BaseUri;
            clone.CorrelationId = CorrelationId;
            clone.RequestHeaders = new Dictionary<string, StringValues>(requestHeaders);
            clone.ResponseHeaders = new Dictionary<string, StringValues>(responseHeaders);
            clone.RouteName = RouteName;
            clone.AuditEventType = AuditEventType;
            clone.Principal = Principal?.Clone() ?? new ClaimsPrincipal();
            clone.ResourceType = ResourceType;
            clone.IncludePartiallyIndexedSearchParams = IncludePartiallyIndexedSearchParams;
            clone.ExecutingBatchOrTransaction = ExecutingBatchOrTransaction;
            clone.IsBackgroundTask = IsBackgroundTask;
            clone.AccessControlContext = AccessControlContext == null ? new AccessControlContext() : (AccessControlContext)AccessControlContext.Clone();
            clone.LogicalPartitionId = LogicalPartitionId;
            clone.PartitionName = PartitionName;

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
