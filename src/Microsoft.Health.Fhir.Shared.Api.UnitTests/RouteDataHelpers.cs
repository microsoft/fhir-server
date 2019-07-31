// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Core.Features.Context;
using NSubstitute;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests
{
    internal class RouteDataHelpers
    {
        internal static RouteData SetupRouteData(IFhirRequestContext fhirRequestContext, HttpContext httpContext, string controllerName, string actionName)
        {
            fhirRequestContext.RouteName.Returns((string)null);

            var routeData = new RouteData();

            routeData.Values.Add("controller", controllerName);
            routeData.Values.Add("action", actionName);

            IRoutingFeature routingFeature = Substitute.For<IRoutingFeature>();

            routingFeature.RouteData.Returns(routeData);

            httpContext.Features[typeof(IRoutingFeature)] = routingFeature;

            return routeData;
        }
    }
}
