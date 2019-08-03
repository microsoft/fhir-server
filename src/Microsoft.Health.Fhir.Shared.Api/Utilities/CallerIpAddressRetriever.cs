// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Utilities;

namespace Microsoft.Health.Fhir.Api.Utilities
{
    public class CallerIpAddressRetriever : ICallerIpAddressRetriever
    {
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CallerIpAddressRetriever(IFhirRequestContextAccessor fhirRequestContextAccessor, IHttpContextAccessor httpContextAccessor)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _httpContextAccessor = httpContextAccessor;
        }

        public string CallerIpAddress
        {
            get
            {
                // Determine the caller IP address.
                // If the X-Forward-For header is supplied, use that; otherwise, get the IP from the HTTP context.
                return _fhirRequestContextAccessor.FhirRequestContext.RequestHeaders.TryGetValue("X-Forwarded-For", out StringValues value) && value.Any() ?
                    value.First() :
                    _httpContextAccessor.HttpContext.Connection.RemoteIpAddress?.ToString();
            }
        }
    }
}
