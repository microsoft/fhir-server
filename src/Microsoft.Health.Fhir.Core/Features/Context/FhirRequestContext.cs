// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Security.Claims;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public class FhirRequestContext : IFhirRequestContext
    {
        private readonly string _uriString;
        private readonly string _baseUriString;

        private Uri _uri;
        private Uri _baseUri;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:Uri parameters should not be strings", Justification = "Lazily initialized to avoid unnecessary allocation.")]
        public FhirRequestContext(
            string method,
            string uriString,
            string baseUriString,
            Coding requestType,
            string correlationId,
            IDictionary<string, StringValues> requestHeaders,
            IDictionary<string, StringValues> responseHeaders)
        {
            EnsureArg.IsNotNullOrWhiteSpace(method, nameof(method));
            EnsureArg.IsNotNullOrWhiteSpace(uriString, nameof(uriString));
            EnsureArg.IsNotNullOrWhiteSpace(baseUriString, nameof(baseUriString));
            EnsureArg.IsNotNull(requestType, nameof(requestType));
            EnsureArg.IsNotNullOrWhiteSpace(correlationId, nameof(correlationId));
            EnsureArg.IsNotNull(responseHeaders, nameof(responseHeaders));
            EnsureArg.IsNotNull(requestHeaders, nameof(requestHeaders));

            Method = method;
            _uriString = uriString;
            _baseUriString = baseUriString;
            RequestType = requestType;
            CorrelationId = correlationId;
            RequestHeaders = requestHeaders;
            ResponseHeaders = responseHeaders;
        }

        public string Method { get; }

        public Uri BaseUri => _baseUri ?? (_baseUri = new Uri(_baseUriString));

        public Uri Uri => _uri ?? (_uri = new Uri(_uriString));

        public string CorrelationId { get; }

        public Coding RequestType { get; }

        public Coding RequestSubType { get; set; }

        public string RouteName { get; set; }

        public ClaimsPrincipal Principal { get; set; }

        public IDictionary<string, StringValues> RequestHeaders { get; }

        public IDictionary<string, StringValues> ResponseHeaders { get; }

        public Expression AuthorizationExpression { get; set; }
    }
}
