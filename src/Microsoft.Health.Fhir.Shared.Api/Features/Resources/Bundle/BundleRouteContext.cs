// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Utility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Shared.Core.Features.Search;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public class BundleRouteContext : IRouteContext
    {
        private readonly EndpointDataSource _endpointDataSource;

        public BundleRouteContext(EndpointDataSource endpointDataSource)
        {
            _endpointDataSource = EnsureArg.IsNotNull(endpointDataSource, nameof(endpointDataSource));
        }

        public RouteContext CreateRouteContext(HttpContext httpContext)
        {
            var routeValues = new RouteValueDictionary();
            var routeEndpoints = _endpointDataSource.Endpoints.OfType<RouteEndpoint>();
            var path = httpContext.Request.Path;
            var method = httpContext.Request.Method;

            foreach (var endpoint in routeEndpoints)
            {
                var templateMatcher = new TemplateMatcher(TemplateParser.Parse(endpoint.RoutePattern.RawText), new RouteValueDictionary());
                if (!templateMatcher.TryMatch(path, routeValues))
                {
                    continue;
                }

                var httpMethodAttribute = endpoint.Metadata.GetMetadata<HttpMethodAttribute>();
                if (httpMethodAttribute != null && !httpMethodAttribute.HttpMethods.Any(x => x.Equals(method, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                bool isConditionalHeader = httpContext.Request.Headers.TryGetValue(KnownHeaders.IfNoneExist, out var value);
                if (isConditionalHeader && endpoint.DisplayName.Contains("Conditional", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (endpoint.RoutePattern?.RequiredValues != null)
                {
                    foreach (var requiredValue in endpoint.RoutePattern.RequiredValues)
                    {
                        routeValues.Add(requiredValue.Key, requiredValue.Value);
                    }
                }

                RouteContext routeContext = new RouteContext(httpContext)
                {
                    Handler = endpoint.RequestDelegate,
                    RouteData = new RouteData(routeValues),
                };

                return routeContext;
            }

            return new RouteContext(httpContext);
        }
    }
}
