// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
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
                string[] conditionalHeaderNames =
                {
                    HeaderNames.IfMatch,
                    HeaderNames.IfModifiedSince,
                    HeaderNames.IfNoneMatch,
                    KnownHeaders.IfNoneExist,
                };

                var routeCandidates = new List<KeyValuePair<RouteEndpoint, RouteValueDictionary>>();
                var endpoints = _endpointDataSource.Endpoints.OfType<RouteEndpoint>();
                var path = context.HttpContext.Request.Path;
                var method = context.HttpContext.Request.Method;
                var conditional = conditionalHeaderNames.Any(name => context.HttpContext.Request.Headers?.ContainsKey(name) ?? false);

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

                    if (!CheckConsumesAttribueIfPresent(context.HttpContext, endpoint))
                    {
                        continue;
                    }

                    routeCandidates.Add(new KeyValuePair<RouteEndpoint, RouteValueDictionary>(endpoint, routeValues));
                }

                if (routeCandidates.Count > 0)
                {
                    RouteEndpoint routeEndpointBestMatch = null;
                    RouteValueDictionary routeValuesBestMatch = null;
                    if (routeCandidates.Count == 1)
                    {
                        routeEndpointBestMatch = routeCandidates[0].Key;
                        routeValuesBestMatch = routeCandidates[0].Value;
                    }
                    else
                    {
                        if (method.Equals(HttpMethods.Post, StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (var candidate in routeCandidates)
                            {
                                if (conditional == (candidate.Key.RoutePattern?.RequiredValues?["action"]?.ToString()?.Contains("Conditional", StringComparison.OrdinalIgnoreCase) ?? false))
                                {
                                    routeEndpointBestMatch = candidate.Key;
                                    routeValuesBestMatch = candidate.Value;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            var sanitizedMethod = HttpUtility.HtmlEncode(method?.Trim());
                            _logger.LogWarning("Multiple endpoint candidates {Count} found for {HttpMethod}.", routeCandidates.Count, sanitizedMethod);
                        }
                    }

                    if (routeEndpointBestMatch != null && routeValuesBestMatch != null)
                    {
                        if (routeEndpointBestMatch.RoutePattern?.RequiredValues != null)
                        {
                            foreach (var requiredValue in routeEndpointBestMatch.RoutePattern.RequiredValues)
                            {
                                routeValuesBestMatch.Add(requiredValue.Key, requiredValue.Value);
                            }
                        }

                        context.Handler = routeEndpointBestMatch.RequestDelegate;
                        context.RouteData = new RouteData(routeValuesBestMatch);
                        context.HttpContext.Request.RouteValues = routeValuesBestMatch;
                    }
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

        private static bool CheckConsumesAttribueIfPresent(HttpContext context, RouteEndpoint endpoint)
        {
            // Request content-type
            var requestContentType = context.Request.Headers["Content-Type"].FirstOrDefault();

            var consumesAttributes = endpoint.Metadata.OfType<ConsumesAttribute>();
            var consumesAttributeContentTypes = consumesAttributes.SelectMany(attr => attr.ContentTypes).ToList();
            if (consumesAttributeContentTypes.Count > 0)
            {
                return consumesAttributeContentTypes.Contains(requestContentType);
            }

            return true;
        }
    }
}
