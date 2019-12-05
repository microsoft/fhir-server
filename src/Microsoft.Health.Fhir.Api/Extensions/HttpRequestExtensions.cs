// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Routing;

namespace Microsoft.Health.Fhir.Api.Extensions
{
    public static class HttpRequestExtensions
    {
        /// <summary>
        /// Check to see whether the request is a FHIR request or not.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns><c>true</c> if the request is a FHIR request; otherwise <c>false</c>.</returns>
        public static bool IsFhirRequest(this HttpRequest request)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            return !request.Path.StartsWithSegments(KnownRoutes.HealthCheck, StringComparison.InvariantCultureIgnoreCase) &&
                   !request.Path.StartsWithSegments(KnownRoutes.CustomError, StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
