// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Security.Claims;
using EnsureThat;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public class FhirRequestContext : IFhirRequestContext
    {
        private readonly string _uriString;
        private readonly string _baseUriString;

        private Uri _uri;
        private Uri _baseUri;
        private IDictionary<string, object> _properties;

        public FhirRequestContext(
            string method,
            string uriString,
            string baseUriString,
            string correlationId,
            IDictionary<string, StringValues> requestHeaders,
            IDictionary<string, StringValues> responseHeaders)
        {
            EnsureArg.IsNotNullOrWhiteSpace(method, nameof(method));
            EnsureArg.IsNotNullOrWhiteSpace(uriString, nameof(uriString));
            EnsureArg.IsNotNullOrWhiteSpace(baseUriString, nameof(baseUriString));
            EnsureArg.IsNotNullOrWhiteSpace(correlationId, nameof(correlationId));
            EnsureArg.IsNotNull(responseHeaders, nameof(responseHeaders));

            Method = method;
            _uriString = uriString;
            _baseUriString = baseUriString;
            CorrelationId = correlationId;
            RequestHeaders = requestHeaders;
            ResponseHeaders = responseHeaders;
            IncludePartiallyIndexedSearchParams = false;
        }

        public string Method { get; }

        public Uri BaseUri => _baseUri ?? (_baseUri = new Uri(_baseUriString));

        public Uri Uri => _uri ?? (_uri = new Uri(_uriString));

        public string CorrelationId { get; }

        public string RouteName { get; set; }

        public string AuditEventType { get; set; }

        public ClaimsPrincipal Principal { get; set; }

        public IDictionary<string, StringValues> RequestHeaders { get; }

        public IDictionary<string, StringValues> ResponseHeaders { get; }

        public IList<OperationOutcomeIssue> BundleIssues { get; } = new List<OperationOutcomeIssue>();

        public string ResourceType { get; set; }

        public bool IncludePartiallyIndexedSearchParams { get; set; }

        public bool ExecutingBatchOrTransaction { get; set; }

        public bool IsBackgroundTask { get; set; }

        public IDictionary<string, object> Properties => _properties ??= new Dictionary<string, object>();

        public AccessControlContext AccessControlContext { get; set; } = new AccessControlContext();

        public object Clone()
        {
            KeyValuePair<string, StringValues>[] requestHeaders = new KeyValuePair<string, StringValues>[RequestHeaders.Count];
            RequestHeaders.CopyTo(requestHeaders, 0);

            KeyValuePair<string, StringValues>[] responseHeaders = new KeyValuePair<string, StringValues>[ResponseHeaders.Count];
            ResponseHeaders.CopyTo(responseHeaders, 0);

            FhirRequestContext clone = new FhirRequestContext(
                Method,
                _uriString,
                _baseUriString,
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
