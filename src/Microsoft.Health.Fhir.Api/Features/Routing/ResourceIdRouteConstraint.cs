// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    public class ResourceIdRouteConstraint : IRouteConstraint
    {
        public bool Match(HttpContext httpContext, IRouter route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
        {
            EnsureArg.IsNotNull(httpContext, nameof(httpContext));
            EnsureArg.IsNotNull(route, nameof(route));
            EnsureArg.IsNotNullOrEmpty(routeKey, nameof(routeKey));
            EnsureArg.IsNotNull(values, nameof(values));

            if (values.TryGetValue(KnownActionParameterNames.Id, out var resourceId) && !string.IsNullOrEmpty(resourceId?.ToString()))
            {
                return Regex.IsMatch(resourceId?.ToString(), "^[A-Za-z0-9\\-\\.]{1,64}$", RegexOptions.Singleline | RegexOptions.Compiled);
            }

            return false;
        }
    }
}
