// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using AngleSharp.Io;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Features.ActionConstraints;
using Microsoft.Health.Fhir.Core.Features;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    internal class BundleRouter : IRouter
    {
        private readonly EndpointDataSource _endpointDataSource;
        private readonly ILogger<BundleRouter> _logger;

        public BundleRouter(EndpointDataSource endpointDataSource, ILogger<BundleRouter> logger)
        {
            EnsureArg.IsNotNull(endpointDataSource, nameof(endpointDataSource));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _endpointDataSource = endpointDataSource;
            _logger = logger;
        }

        public VirtualPathData GetVirtualPath(VirtualPathContext context)
        {
            throw new System.NotImplementedException();
        }

        public Task RouteAsync(RouteContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            SetRouteContext(context);
            return Task.CompletedTask;
        }

        private void SetRouteContext(RouteContext context)
        {
            try
            {
                var routeCandidates = new List<KeyValuePair<RouteEndpoint, RouteValueDictionary>>();
                var endpoints = _endpointDataSource.Endpoints.OfType<RouteEndpoint>();
                var path = context.HttpContext.Request.Path;
                var method = context.HttpContext.Request.Method;

                foreach (var endpoint in endpoints)
                {
                    var routeValues = new RouteValueDictionary();
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

                    routeCandidates.Add(new KeyValuePair<RouteEndpoint, RouteValueDictionary>(endpoint, routeValues));
                }

                (RouteEndpoint routeEndpointMatch, RouteValueDictionary routeValuesMatch) = FindRouteEndpoint(context.HttpContext, routeCandidates);
                if (routeEndpointMatch != null && routeValuesMatch != null)
                {
                    if (routeEndpointMatch.RoutePattern?.RequiredValues != null)
                    {
                        foreach (var requiredValue in routeEndpointMatch.RoutePattern.RequiredValues)
                        {
                            routeValuesMatch.Add(requiredValue.Key, requiredValue.Value);
                        }
                    }

                    context.Handler = routeEndpointMatch.RequestDelegate;
                    context.RouteData = new RouteData(routeValuesMatch);
                    context.HttpContext.Request.RouteValues = routeValuesMatch;
                }

                if (context.Handler == null)
                {
                    _logger.LogError("Matching route endpoint not found for the given request.");
                }
            }
            catch
            {
                throw;
            }
        }

        private static (RouteEndpoint routeEndpoint, RouteValueDictionary routeValues) FindRouteEndpoint(
            HttpContext context,
            IList<KeyValuePair<RouteEndpoint, RouteValueDictionary>> routeCandidates)
        {
            if (routeCandidates.Count == 0)
            {
                return (null, null);
            }

            if (routeCandidates.Count == 1)
            {
                return (routeCandidates[0].Key, routeCandidates[0].Value);
            }

            // Note: When there are more than one route endpoint candidates, we need to find the best match
            //       by looking at the request method, path, and headers. The logic of finding the best match
            //       as of now is based on the implementation of FhirController actions and attributes.
            // TODO: Find a more generic way of implementing the logic.

            var method = context.Request.Method;
            if (method.Equals(HttpMethods.Post, StringComparison.OrdinalIgnoreCase))
            {
                var conditional = context.Request.Headers.ContainsKey(KnownHeaders.IfNoneExist);
                var pair = routeCandidates.SingleOrDefault(r => (r.Key.Metadata.GetMetadata<ConditionalConstraintAttribute>() != null) == conditional);
                return (pair.Key, pair.Value);
            }
            else if (method.Equals(HttpMethods.Patch, StringComparison.OrdinalIgnoreCase))
            {
                var contentType = context.Request.Headers.ContentType;
                foreach (var candidate in routeCandidates)
                {
                    var consumes = candidate.Key.Metadata.GetMetadata<ConsumesAttribute>();
                    if (consumes != null && consumes.ContentTypes.Any(t => string.Equals(t, contentType, StringComparison.OrdinalIgnoreCase)))
                    {
                        return (candidate.Key, candidate.Value);
                    }
                }
            }

            return (null, null);
        }
    }
}
