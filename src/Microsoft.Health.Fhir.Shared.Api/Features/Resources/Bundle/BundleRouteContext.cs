// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
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

        public void CreateRouteContext(RouteContext routeContext)
        {
            var routeValues = new RouteValueDictionary();
            var httpContext = routeContext.HttpContext;
            var routeEndpoints = _endpointDataSource.Endpoints.OfType<RouteEndpoint>();

            foreach (var endpoint in routeEndpoints)
            {
                if (IsEndpointMatch(endpoint, httpContext, routeValues))
                {
                    if (endpoint.RoutePattern?.RequiredValues != null)
                    {
                        foreach (var requiredValue in endpoint.RoutePattern.RequiredValues)
                        {
                            routeValues.Add(requiredValue.Key, requiredValue.Value);
                        }
                    }

                    routeContext.Handler = endpoint.RequestDelegate;
                    routeContext.RouteData = new RouteData(routeValues);

                    break;
                }
            }
        }

        private static bool IsEndpointMatch(RouteEndpoint routeEndpoint, HttpContext context, RouteValueDictionary routeValues)
        {
            var template = TemplateParser.Parse(routeEndpoint.RoutePattern.RawText);

            var matcher = new TemplateMatcher(template, GetDefaults(template));

            bool matchedPath = matcher.TryMatch(context.Request.Path, routeValues);
            var httpMethodMetadata = routeEndpoint.Metadata.GetMetadata<HttpMethodMetadata>();

            if (matchedPath)
            {
                var requestMethod = context.Request.Method;
                if (httpMethodMetadata != null && httpMethodMetadata.HttpMethods.Contains(requestMethod))
                {
                    bool isConditionalHeader = context.Request.Headers.TryGetValue(KnownHeaders.IfNoneExist, out var value);
                    if (!isConditionalHeader)
                    {
                        return true;
                    }
                    else if (routeEndpoint.DisplayName.Contains("Conditional", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // This method extracts the default argument values from the template.
        private static RouteValueDictionary GetDefaults(RouteTemplate parsedTemplate)
        {
            var result = new RouteValueDictionary();

            foreach (var parameter in parsedTemplate.Parameters)
            {
                if (parameter.DefaultValue != null)
                {
                    result.Add(parameter.Name, parameter.DefaultValue);
                }
            }

            return result;
        }
    }
}
