// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Filters.Metrics
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SearchEndpointMetricEmitterAttribute : BaseEndpointMetricEmitterAttribute
    {
        private readonly ISearchMetricHandler _metricHandler;
        private readonly ILogger<SearchEndpointMetricEmitterAttribute> _logger;

        public SearchEndpointMetricEmitterAttribute(ISearchMetricHandler metricHandler)
            : this(metricHandler, NullLogger<SearchEndpointMetricEmitterAttribute>.Instance)
        {
        }

        public SearchEndpointMetricEmitterAttribute(
            ISearchMetricHandler metricHandler,
            ILogger<SearchEndpointMetricEmitterAttribute> logger)
        {
            EnsureArg.IsNotNull(metricHandler, nameof(metricHandler));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _metricHandler = metricHandler;
            _logger = logger;
        }

        // Under the scope of bundle requests, this methid is not called and no nested metrics are emitted.
        public override void EmitMetricOnActionExecuted(ActionExecutedContext context, ActionExecutedStatistics statistics)
        {
            SearchComplexity complexity = DetermineComplexity(context, _logger);

            _metricHandler.EmitLatency(new SearchMetricNotification
            {
                ElapsedMilliseconds = statistics.ElapsedMilliseconds,
                Complexity = complexity,
            });
        }

        internal static SearchComplexity DetermineComplexity(ActionExecutedContext context)
        {
            return DetermineComplexity(context, null);
        }

        internal static SearchComplexity DetermineComplexity(ActionExecutedContext context, ILogger logger)
        {
            if (context?.ActionDescriptor?.RouteValues == null)
            {
                return SearchComplexity.Low;
            }

            try
            {
                context.ActionDescriptor.RouteValues.TryGetValue("action", out string actionName);
                context.ActionDescriptor.RouteValues.TryGetValue("controller", out string controllerName);

                // System History is inherently expensive (scans all resource versions) — High.
                if (string.Equals(actionName, "SystemHistory", StringComparison.OrdinalIgnoreCase))
                {
                    return SearchComplexity.High;
                }

                // $everything, $includes, $docref are multi-step operations — Medium.
                if (string.Equals(controllerName, "Everything", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(controllerName, "Includes", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(controllerName, "DocRef", StringComparison.OrdinalIgnoreCase))
                {
                    return SearchComplexity.Medium;
                }

                // Searches with multiple _include/_revinclude require additional DB round-trips — Medium.
                if (context.HttpContext?.Request?.Query != null)
                {
                    int includeCount = 0;
                    foreach (string key in context.HttpContext.Request.Query.Keys.Where(
                        key => string.Equals(key, "_include", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(key, "_revinclude", StringComparison.OrdinalIgnoreCase)))
                    {
                        includeCount += context.HttpContext.Request.Query[key].Count;
                    }

                    if (includeCount > 1)
                    {
                        return SearchComplexity.Medium;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to determine search complexity. Defaulting to Low.");
                return SearchComplexity.Low;
            }

            // Standard Search, Type Get All, Type History, Instance History.
            return SearchComplexity.Low;
        }
    }
}
