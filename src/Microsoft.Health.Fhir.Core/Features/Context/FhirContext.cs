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

        public string HttpMethod { get; set; }

        public Uri RequestUri { get; set; }

        public string RouteName { get; set; }

        public ClaimsPrincipal Principal { get; set; }

        public IDictionary<string, StringValues> RequestHeaders { get; set; }

        public IDictionary<string, StringValues> ResponseHeaders { get; set; }
    }
}
