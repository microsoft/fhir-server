// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Claims;
using System.Threading;
using EnsureThat;
using Hl7.Fhir.Model;

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
            string correlationId)
        {
            EnsureArg.IsNotNullOrWhiteSpace(method, nameof(method));
            EnsureArg.IsNotNullOrWhiteSpace(uriString, nameof(uriString));
            EnsureArg.IsNotNullOrWhiteSpace(baseUriString, nameof(baseUriString));
            EnsureArg.IsNotNull(requestType, nameof(requestType));
            EnsureArg.IsNotNullOrWhiteSpace(correlationId, nameof(correlationId));

            Method = method;
            _uriString = uriString;
            _baseUriString = baseUriString;
            RequestType = requestType;
            CorrelationId = correlationId;
        }

        public string Method { get; }

        public Uri BaseUri
        {
            get
            {
                return LazyInitializer.EnsureInitialized(ref _baseUri, () => new Uri(_baseUriString));
            }
        }

        public Uri Uri
        {
            get
            {
                return LazyInitializer.EnsureInitialized(ref _uri, () => new Uri(_uriString));
            }
        }

        public string CorrelationId { get; }

        public Coding RequestType { get; }

        public Coding RequestSubType { get; set; }

        public string RouteName { get; set; }

        public ClaimsPrincipal Principal { get; set; }
    }
}
