// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    /// <summary>
    /// BundleRouter creates the routingContext for bundles with enabled endpoint routing.It fetches all RouteEndpoints using EndpointDataSource(based on controller actions)
    /// and find the best endpoint match based on the request httpContext to build the routeContext for bundle request to route to appropriate action.
    /// </summary>
    internal class BundleRouter : IRouter
    {
        private readonly TemplateBinderFactory _templateBinderFactory;
        private readonly IEndpointSelectorPolicy[] _matcherPolicies;
        private readonly EndpointDataSource _endpointDataSource;
        private readonly EndpointSelector _endpointSelector;
        private readonly ILogger<BundleRouter> _logger;
        private readonly object _routeEndpointCacheLock = new object();

        private volatile RouteEndpointCache _routeEndpointCache;

        public BundleRouter(
            TemplateBinderFactory templateBinderFactory,
            IEnumerable<MatcherPolicy> matcherPolicies,
            EndpointDataSource endpointDataSource,
            EndpointSelector endpointSelector,
            ILogger<BundleRouter> logger)
        {
            EnsureArg.IsNotNull(templateBinderFactory, nameof(templateBinderFactory));
            EnsureArg.IsNotNull(matcherPolicies, nameof(matcherPolicies));
            EnsureArg.IsNotNull(endpointDataSource, nameof(endpointDataSource));
            EnsureArg.IsNotNull(endpointSelector, nameof(endpointSelector));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _templateBinderFactory = templateBinderFactory;
            _matcherPolicies = matcherPolicies
                .OrderBy(x => x.Order)
                .OfType<IEndpointSelectorPolicy>()
                .ToArray();
            _endpointDataSource = endpointDataSource;
            _endpointSelector = endpointSelector;
            _logger = logger;
        }

        public VirtualPathData GetVirtualPath(VirtualPathContext context)
        {
            throw new System.NotImplementedException();
        }

        public async Task RouteAsync(RouteContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            RouteEndpointCache routeEndpointCache = GetRouteEndpointCache();
            CandidateSet candidateSet = CreateCandidateSet(context, routeEndpointCache.RouteEndpoints);

            // Policies apply filters / matches on attributes such as Consumes, HttpVerbs etc...
            foreach (IEndpointSelectorPolicy policy in _matcherPolicies)
            {
                await policy.ApplyAsync(context.HttpContext, candidateSet);
            }

            await _endpointSelector.SelectAsync(context.HttpContext, candidateSet);

            Endpoint selectedEndpoint = context.HttpContext.GetEndpoint();

            // A RouteEndpoint should map to an MVC controller.
            // When this isn't a RouteEndpoint it can be a 404 or a middleware endpoint mapping.
            if (selectedEndpoint is RouteEndpoint)
            {
                RouteData data = context.HttpContext.GetRouteData();
                context.Handler = selectedEndpoint.RequestDelegate;
                context.RouteData = new RouteData(data);
                context.HttpContext.Request.RouteValues = context.RouteData.Values;
            }
            else
            {
                _logger.LogDebug("No RouteEndpoint found for '{Path}'", HttpUtility.UrlEncode(context.HttpContext.Request.Path));
            }
        }

        private CandidateSet CreateCandidateSet(RouteContext context, RouteEndpointMatcher[] routeEndpoints)
        {
            Endpoint[] endpoints = new Endpoint[routeEndpoints.Length];
            RouteValueDictionary[] routeValues = new RouteValueDictionary[routeEndpoints.Length];
            int[] scores = new int[routeEndpoints.Length];
            int candidateCount = 0;
            PathString path = context.HttpContext.Request.Path;

            foreach (RouteEndpointMatcher routeEndpoint in routeEndpoints)
            {
                var candidateRouteValues = new RouteValueDictionary();

                if (!routeEndpoint.TemplateMatcher.TryMatch(path, candidateRouteValues))
                {
                    continue;
                }

                // Eliminate routes that don't match constraints.
                if (!routeEndpoint.TemplateBinder.TryProcessConstraints(context.HttpContext, candidateRouteValues, out var parameterName, out IRouteConstraint constraint))
                {
                    _logger.LogDebug("Constraint '{ConstraintType}' not met for parameter '{ParameterName}'", constraint, parameterName);
                    continue;
                }

                endpoints[candidateCount] = routeEndpoint.Endpoint;
                routeValues[candidateCount] = candidateRouteValues;
                scores[candidateCount] = 1;
                candidateCount++;
            }

            if (candidateCount != routeEndpoints.Length)
            {
                Array.Resize(ref endpoints, candidateCount);
                Array.Resize(ref routeValues, candidateCount);
                Array.Resize(ref scores, candidateCount);
            }

            return new CandidateSet(endpoints, routeValues, scores);
        }

        private RouteEndpointCache GetRouteEndpointCache()
        {
            RouteEndpointCache routeEndpointCache = _routeEndpointCache;
            if (routeEndpointCache?.ChangeToken.HasChanged == false)
            {
                return routeEndpointCache;
            }

            lock (_routeEndpointCacheLock)
            {
                routeEndpointCache = _routeEndpointCache;
                if (routeEndpointCache?.ChangeToken.HasChanged == false)
                {
                    return routeEndpointCache;
                }

                routeEndpointCache = CreateRouteEndpointCache();
                _routeEndpointCache = routeEndpointCache;
                return routeEndpointCache;
            }
        }

        private RouteEndpointCache CreateRouteEndpointCache()
        {
            IChangeToken changeToken = _endpointDataSource.GetChangeToken();
            RouteEndpointMatcher[] routeEndpoints = _endpointDataSource.Endpoints
                .OfType<RouteEndpoint>()
                .Select(endpoint =>
                {
                    RoutePattern pattern = endpoint.RoutePattern;
                    var routeDefaults = new RouteValueDictionary(pattern.Defaults);

                    return new RouteEndpointMatcher(
                        endpoint,
                        _templateBinderFactory.Create(pattern),
                        new TemplateMatcher(new RouteTemplate(pattern), routeDefaults));
                })
                .ToArray();

            return new RouteEndpointCache(changeToken, routeEndpoints);
        }

        private sealed class RouteEndpointCache
        {
            public RouteEndpointCache(IChangeToken changeToken, RouteEndpointMatcher[] routeEndpoints)
            {
                ChangeToken = changeToken;
                RouteEndpoints = routeEndpoints;
            }

            public IChangeToken ChangeToken { get; }

            public RouteEndpointMatcher[] RouteEndpoints { get; }
        }

        private sealed class RouteEndpointMatcher
        {
            public RouteEndpointMatcher(RouteEndpoint endpoint, TemplateBinder templateBinder, TemplateMatcher templateMatcher)
            {
                Endpoint = endpoint;
                TemplateBinder = templateBinder;
                TemplateMatcher = templateMatcher;
            }

            public RouteEndpoint Endpoint { get; }

            public TemplateBinder TemplateBinder { get; }

            public TemplateMatcher TemplateMatcher { get; }
        }
    }
}
