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

        public FhirRequestContext(
            string method,
            string uriString,
            string baseUriString,
            string correlationId,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            IDictionary<string, StringValues> requestHeaders,
            IDictionary<string, StringValues> responseHeaders)
        {
            EnsureArg.IsNotNullOrWhiteSpace(method, nameof(method));
            EnsureArg.IsNotNullOrWhiteSpace(uriString, nameof(uriString));
            EnsureArg.IsNotNullOrWhiteSpace(baseUriString, nameof(baseUriString));
            EnsureArg.IsNotNullOrWhiteSpace(correlationId, nameof(correlationId));
            EnsureArg.IsNotNull(queryParameters, nameof(queryParameters));
            EnsureArg.IsNotNull(responseHeaders, nameof(responseHeaders));

            Method = method;
            _uriString = uriString;
            _baseUriString = baseUriString;
            CorrelationId = correlationId;
            QueryParameters = queryParameters;
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

        public IReadOnlyList<Tuple<string, string>> QueryParameters { get; }

        public IDictionary<string, StringValues> RequestHeaders { get; }

        public IDictionary<string, StringValues> ResponseHeaders { get; }

        public IList<OperationOutcomeIssue> BundleIssues { get; } = new List<OperationOutcomeIssue>();

        public string ResourceType { get; set; }

        public bool IncludePartiallyIndexedSearchParams { get; set; }
    }
}
