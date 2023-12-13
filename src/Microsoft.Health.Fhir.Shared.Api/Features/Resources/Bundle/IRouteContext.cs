// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public interface IRouteContext
    {
        /// <summary>
        /// Updates the routeContext for the matched RouteEndpoint.
        /// </summary>
        /// <param name="routeContext">HttpContext of the current request.</param>
        void UpdateRouteContext(RouteContext routeContext);
    }
}
