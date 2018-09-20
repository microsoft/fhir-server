// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Claims;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public interface IFhirRequestContext
    {
        string Method { get; }

        string Scheme { get; }

        string Host { get; }

        int? Port { get; }

        string PathBase { get; }

        string Path { get; }

        string QueryString { get; }

        Uri Uri { get; }

        Uri BaseUri { get; }

        string CorrelationId { get; }

        Coding RequestType { get; }

        Coding RequestSubType { get; set; }

        string RouteName { get; set; }

        ClaimsPrincipal Principal { get; set; }
    }
}
