// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Security.Claims;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public interface IFhirContext
    {
        string CorrelationId { get; }

        Coding RequestType { get; set; }

        Coding RequestSubType { get; set; }

        HttpMethod HttpMethod { get; set; }

        Uri RequestUri { get; set; }

        string RouteName { get; set; }

        ClaimsPrincipal Principal { get; set; }
    }
}
