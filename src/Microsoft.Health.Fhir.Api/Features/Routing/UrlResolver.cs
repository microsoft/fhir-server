// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    /// <summary>
    /// Provides functionality to resolve URLs.
    /// </summary>
    public class UrlResolver : IUrlResolver
    {
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IUrlHelperFactory _urlHelperFactory;

        // If we update the search implementation to not use these, we should remove
        // the registration since enabling these accessors has performance implications.
        // https://github.com/aspnet/Hosting/issues/793
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IActionContextAccessor _actionContextAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="UrlResolver"/> class.
        /// </summary>
        /// <param name="fhirRequestContextAccessor">The FHIR request context accessor.</param>
        /// <param name="urlHelperFactory">The ASP.NET Core URL helper factory.</param>
        /// <param name="httpContextAccessor">The ASP.NET Core HTTP context accessor.</param>
        /// <param name="actionContextAccessor">The ASP.NET Core Action context accessor.</param>
        public UrlResolver(
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IUrlHelperFactory urlHelperFactory,
            IHttpContextAccessor httpContextAccessor,
            IActionContextAccessor actionContextAccessor)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(urlHelperFactory, nameof(urlHelperFactory));
            EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));
            EnsureArg.IsNotNull(actionContextAccessor, nameof(actionContextAccessor));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _urlHelperFactory = urlHelperFactory;
            _httpContextAccessor = httpContextAccessor;
            _actionContextAccessor = actionContextAccessor;
        }

        private HttpRequest Request => _httpContextAccessor.HttpContext.Request;

        private ActionContext ActionContext => _actionContextAccessor.ActionContext;

        private IUrlHelper UrlHelper => _urlHelperFactory.GetUrlHelper(ActionContext);

        /// <inheritdoc />
        public Uri ResolveMetadataUrl(bool includeSystemQueryString)
        {
            var routeValues = new RouteValueDictionary();

            if (includeSystemQueryString)
            {
                routeValues.Add("system", true);
            }

            var uriString = UrlHelper.RouteUrl(
                RouteNames.Metadata,
                routeValues,
                Request.Scheme,
                Request.Host.Value);

            return new Uri(uriString);
        }

        public Uri ResolveResourceUrl(ResourceElement resource, bool includeVersion = false)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var routeName = RouteNames.ReadResource;
            var routeValues = new RouteValueDictionary
            {
                { KnownActionParameterNames.ResourceType, resource.InstanceType },
                { KnownActionParameterNames.Id, resource.Id },
            };

            if (includeVersion)
            {
                routeName = RouteNames.ReadResourceWithVersionRoute;
                routeValues.Add(KnownActionParameterNames.Vid, resource.VersionId);
            }

            var uriString = UrlHelper.RouteUrl(
                routeName,
                routeValues,
                Request.Scheme,
                Request.Host.Value);

            return new Uri(uriString);
        }

        public Uri ResolveRouteUrl(IEnumerable<Tuple<string, string>> unsupportedSearchParams = null, IReadOnlyList<(string parameterName, string reason)> unsupportedSortingParameters = null, string continuationToken = null)
        {
            string routeName = _fhirRequestContextAccessor.FhirRequestContext.RouteName;

            Debug.Assert(!string.IsNullOrWhiteSpace(routeName), "The routeName should not be null or empty.");

            var routeValues = new RouteValueDictionary();

            // We could have multiple query parameters with the same name. In this case, we should only remove
            // the query parameter that was not used or not supported.
            ILookup<string, string> searchParamsToRemove = (unsupportedSearchParams ?? Enumerable.Empty<Tuple<string, string>>())
                .ToLookup(sp => sp.Item1, sp => sp.Item2, StringComparer.OrdinalIgnoreCase);

            // Add all query parameters except those that were not used.
            if (Request.Query != null)
            {
                foreach (KeyValuePair<string, StringValues> searchParam in Request.Query)
                {
                    // Remove the parameter if:
                    // 1. It is the continuation token (if there is a new continuation token, then it will be added again).
                    // 2. It is the _sort parameter and the parameter is not supported for sorting
                    // 3. The parameter is not supported.
                    if (!string.Equals(searchParam.Key, KnownQueryParameterNames.ContinuationToken, StringComparison.OrdinalIgnoreCase))
                    {
                        if (unsupportedSortingParameters?.Count > 0 && string.Equals(searchParam.Key, KnownQueryParameterNames.Sort, StringComparison.OrdinalIgnoreCase))
                        {
                            var filteredValues = new List<string>(searchParam.Value.Count);

                            foreach (string stringValue in searchParam.Value)
                            {
                                // parameters are separated by a comma and can be prefixed with a dash to indicate descending order
                                string[] tokens = stringValue.Split(',');

                                List<string> values = tokens.Where(sortValue =>
                                        !unsupportedSortingParameters.Any(p =>
                                            sortValue.EndsWith(p.parameterName, StringComparison.Ordinal) &&
                                            (sortValue.Length == p.parameterName.Length ||
                                             (sortValue.Length > 0 && sortValue.Length == p.parameterName.Length + 1 && sortValue[0] == '-'))))
                                    .ToList();

                                if (values.Count == tokens.Length)
                                {
                                    filteredValues.Add(stringValue);
                                }
                                else if (values.Count == 1)
                                {
                                    filteredValues.Add(values[0]);
                                }
                                else if (values.Count > 0)
                                {
                                    filteredValues.Add(string.Join(',', values));
                                }
                            }

                            if (filteredValues.Count > 0)
                            {
                                routeValues.Add(searchParam.Key, filteredValues.Count == 1 ? new StringValues(filteredValues[0]) : new StringValues(filteredValues.ToArray()));
                            }
                        }
                        else
                        {
                            IEnumerable<string> removedValues = searchParamsToRemove[searchParam.Key];

                            StringValues usedValues = removedValues.Any()
                                ? new StringValues(searchParam.Value.Except(removedValues).ToArray())
                                : searchParam.Value;

                            if (usedValues.Any())
                            {
                                routeValues.Add(searchParam.Key, usedValues);
                            }
                        }
                    }
                }
            }

            if (continuationToken != null)
            {
                routeValues[KnownQueryParameterNames.ContinuationToken] = continuationToken;
            }

            string uriString = UrlHelper.RouteUrl(
                routeName,
                routeValues,
                Request.Scheme,
                Request.Host.Value);

            return new Uri(uriString);
        }

        public Uri ResolveRouteNameUrl(string routeName, IDictionary<string, object> routeValues)
        {
            var routeValueDictionary = new RouteValueDictionary(routeValues);

            var uriString = UrlHelper.RouteUrl(
                routeName,
                routeValueDictionary,
                Request.Scheme,
                Request.Host.Value);

            return new Uri(uriString);
        }

        public Uri ResolveOperationResultUrl(string operationName, string id)
        {
            EnsureArg.IsNotNullOrWhiteSpace(operationName, nameof(operationName));
            EnsureArg.IsNotNullOrWhiteSpace(id, nameof(id));

            if (!string.Equals(operationName, OperationsConstants.Export, StringComparison.OrdinalIgnoreCase))
            {
                throw new OperationNotImplementedException(string.Format(Resources.OperationNotImplemented, operationName));
            }

            var routeValues = new RouteValueDictionary()
            {
                { KnownActionParameterNames.Id, id },
            };

            string uriString = UrlHelper.RouteUrl(
                RouteNames.GetExportStatusById,
                routeValues,
                Request.Scheme,
                Request.Host.Value);

            return new Uri(uriString);
        }
    }
}
