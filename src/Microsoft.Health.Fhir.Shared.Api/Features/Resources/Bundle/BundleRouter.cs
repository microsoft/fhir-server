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
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Features.ActionConstraints;
using Microsoft.Health.Fhir.Core.Features;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    /* BundleRouter creates the routingContext for bundles with enabled endpoint routing.It fetches all RouteEndpoints using EndpointDataSource(based on controller actions)
       and find the best endpoint match based on the request httpContext to build the routeContext for bundle request to route to appropriate action.*/
    internal class BundleRouter : IRouter
    {
        private readonly TemplateBinderFactory _parameterPolicies;
        private readonly IEnumerable<MatcherPolicy> _matcherPolicies;
        private readonly EndpointDataSource _endpointDataSource;
        private readonly EndpointSelector _endpointSelector;
        private readonly ILogger<BundleRouter> _logger;

        public BundleRouter(
            TemplateBinderFactory parameterPolicies,
            IEnumerable<MatcherPolicy> matcherPolicies,
            EndpointDataSource endpointDataSource,
            EndpointSelector endpointSelector,
            ILogger<BundleRouter> logger)
        {
            EnsureArg.IsNotNull(parameterPolicies, nameof(parameterPolicies));
            EnsureArg.IsNotNull(matcherPolicies, nameof(matcherPolicies));
            EnsureArg.IsNotNull(endpointDataSource, nameof(endpointDataSource));
            EnsureArg.IsNotNull(endpointSelector, nameof(endpointSelector));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _parameterPolicies = parameterPolicies;
            _matcherPolicies = matcherPolicies;
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

            var routeCandidates = new List<KeyValuePair<RouteEndpoint, RouteValueDictionary>>();
            IEnumerable<RouteEndpoint> endpoints = _endpointDataSource.Endpoints.OfType<RouteEndpoint>();
            PathString path = context.HttpContext.Request.Path;

            foreach (RouteEndpoint endpoint in endpoints)
            {
                var routeValues = new RouteValueDictionary();
                var routeDefaults = new RouteValueDictionary(endpoint.RoutePattern.Defaults);

                RoutePattern pattern = endpoint.RoutePattern;
                TemplateBinder templateBinder = _parameterPolicies.Create(pattern);

                var templateMatcher = new TemplateMatcher(new RouteTemplate(pattern), routeDefaults);

                // Pattern match
                if (!templateMatcher.TryMatch(path, routeValues))
                {
                    continue;
                }

                // Eliminate routes that don't match constraints
                if (!templateBinder.TryProcessConstraints(context.HttpContext, routeValues, out var parameterName, out IRouteConstraint constraint))
                {
                    _logger.LogDebug("Constraint '{ConstraintType}' not met for parameter '{ParameterName}'", constraint, parameterName);
                    continue;
                }

                routeCandidates.Add(new KeyValuePair<RouteEndpoint, RouteValueDictionary>(endpoint, routeValues));
            }

            var candidateSet = new CandidateSet(
                routeCandidates.Select(x => x.Key).Cast<Endpoint>().ToArray(),
                routeCandidates.Select(x => x.Value).ToArray(),
                Enumerable.Repeat(1, routeCandidates.Count).ToArray());

            // Covered things like Consumes, HttpVerbs etc...
            foreach (IEndpointSelectorPolicy policy in _matcherPolicies
                         .OrderBy(x => x.Order)
                         .OfType<IEndpointSelectorPolicy>())
            {
                await policy.ApplyAsync(context.HttpContext, candidateSet);
            }

            await _endpointSelector.SelectAsync(context.HttpContext, candidateSet);

            Endpoint selectedEndpoint = context.HttpContext.GetEndpoint();
            if (selectedEndpoint != null)
            {
                context.Handler = selectedEndpoint.RequestDelegate;
                context.RouteData = new RouteData(context.HttpContext.GetRouteData());
                context.HttpContext.Request.RouteValues = context.HttpContext.GetRouteData().Values;
            }
        }
    }
}
