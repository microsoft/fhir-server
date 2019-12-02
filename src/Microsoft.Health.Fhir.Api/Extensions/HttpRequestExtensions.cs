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
        /// Check to see whether the request is for health check or not.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns><c>true</c> if the request is for health check; otherwise <c>false</c>.</returns>
        public static bool IsHealthCheck(this HttpRequest request)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            return request.Path.HasValue && request.Path.StartsWithSegments(KnownRoutes.HealthCheck, StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
