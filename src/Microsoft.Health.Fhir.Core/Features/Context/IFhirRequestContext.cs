// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public interface IFhirRequestContext
    {
        string Method { get; }

        Uri BaseUri { get; }

        Uri Uri { get; }

        string CorrelationId { get; }

        CodingInfo RequestType { get; }

        string RouteName { get; set; }

        ClaimsPrincipal Principal { get; set; }

        IDictionary<string, StringValues> RequestHeaders { get; }

        IDictionary<string, StringValues> ResponseHeaders { get; }
    }
}
