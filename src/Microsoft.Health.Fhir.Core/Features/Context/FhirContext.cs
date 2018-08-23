// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Security.Claims;
using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public class FhirContext : IFhirContext
    {
        public FhirContext(string correlationId)
        {
            EnsureArg.IsNotNullOrEmpty(correlationId, nameof(correlationId));

            CorrelationId = correlationId;
        }

        public string CorrelationId { get; }

        public Coding RequestType { get; set; }

        public Coding RequestSubType { get; set; }

        public HttpMethod HttpMethod { get; set; }

        public Uri RequestUri { get; set; }

        public string RouteName { get; set; }

        public ClaimsPrincipal Principal { get; set; }
    }
}
