﻿// -------------------------------------------------------------------------------------------------
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
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    /// <summary>
    /// Provides functionality to resolve URLs.
    /// </summary>
    public class UrlResolver : IUrlResolver
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IUrlHelperFactory _urlHelperFactory;
        private readonly LinkGenerator _linkGenerator;

        // If we update the search implementation to not use these, we should remove
        // the registration since enabling these accessors has performance implications.
        // https://github.com/aspnet/Hosting/issues/793
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IActionContextAccessor _actionContextAccessor;
        private readonly IBundleHttpContextAccessor _bundleHttpContextAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="UrlResolver"/> class.
        /// </summary>
        /// <param name="fhirRequestContextAccessor">The FHIR request context accessor.</param>
        /// <param name="urlHelperFactory">The ASP.NET Core URL helper factory.</param>
        /// <param name="httpContextAccessor">The ASP.NET Core HTTP context accessor.</param>
        /// <param name="actionContextAccessor">The ASP.NET Core Action context accessor.</param>
        /// <param name="bundleHttpContextAccessor">The bundle aware http context accessor.</param>
        /// <param name="linkGenerator">The ASP.NET Core link generator.</param>
        public UrlResolver(
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IUrlHelperFactory urlHelperFactory,
            IHttpContextAccessor httpContextAccessor,
            IActionContextAccessor actionContextAccessor,
            IBundleHttpContextAccessor bundleHttpContextAccessor,
            LinkGenerator linkGenerator)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(urlHelperFactory, nameof(urlHelperFactory));
            EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));
            EnsureArg.IsNotNull(actionContextAccessor, nameof(actionContextAccessor));
            EnsureArg.IsNotNull(bundleHttpContextAccessor, nameof(bundleHttpContextAccessor));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _urlHelperFactory = urlHelperFactory;
            _httpContextAccessor = httpContextAccessor;
            _actionContextAccessor = actionContextAccessor;
            _bundleHttpContextAccessor = bundleHttpContextAccessor;
            _linkGenerator = linkGenerator;
        }

        private HttpRequest Request
        {
            get
            {
                if (_bundleHttpContextAccessor.HttpContext != null)
                {
                    return _bundleHttpContextAccessor.HttpContext.Request;
                }

                return _httpContextAccessor.HttpContext.Request;
            }
        }

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

            return GetRouteUri(
                ActionContext.HttpContext,
                RouteNames.Metadata,
                routeValues,
                Request.Scheme,
                Request.Host.Value);
        }

        public Uri ResolveResourceUrl(IResourceElement resource, bool includeVersion = false)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            return ResolveResourceUrl(resource.Id, resource.InstanceType, resource.VersionId, includeVersion);
        }

        public Uri ResolveResourceWrapperUrl(ResourceWrapper resource, bool includeVersion = false)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            return ResolveResourceUrl(resource.ResourceId, resource.ResourceTypeName, resource.Version, includeVersion);
        }

        private Uri ResolveResourceUrl(string resourceId, string resourceTypeName, string version, bool includeVersion)
        {
            var routeName = RouteNames.ReadResource;
            var routeValues = new RouteValueDictionary
            {
                { KnownActionParameterNames.ResourceType, resourceTypeName },
                { KnownActionParameterNames.Id, resourceId },
            };

            if (includeVersion)
            {
                routeName = RouteNames.ReadResourceWithVersionRoute;
                routeValues.Add(KnownActionParameterNames.Vid, version);
            }

            return GetRouteUri(
                ActionContext.HttpContext,
                routeName,
                routeValues,
                Request.Scheme,
                Request.Host.Value);
        }

        public Uri ResolveRouteUrl(IEnumerable<Tuple<string, string>> unsupportedSearchParams = null, IReadOnlyList<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)> resultSortOrder = null, string continuationToken = null, bool removeTotalParameter = false)
        {
            string routeName = _fhirRequestContextAccessor.RequestContext.RouteName;

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
                    if (removeTotalParameter && string.Equals(searchParam.Key, KnownQueryParameterNames.Total, StringComparison.OrdinalIgnoreCase))
                    {
                        // Remove the _total parameter, since we only want to count for the first page of results.
                        continue;
                    }

                    if (string.Equals(searchParam.Key, KnownQueryParameterNames.Sort, StringComparison.OrdinalIgnoreCase))
                    {
                        switch (resultSortOrder?.Count)
                        {
                            case null:
                            case 0:
                                break;

                            // rewrite the sort order based on the sort order that was actually applied
                            case 1 when resultSortOrder[0].sortOrder == SortOrder.Ascending:
                                routeValues.Add(searchParam.Key, resultSortOrder[0].searchParameterInfo.Code);
                                break;
                            default:
                                routeValues.Add(searchParam.Key, string.Join(',', resultSortOrder.Select(s => $"{(s.sortOrder == SortOrder.Ascending ? string.Empty : "-")}{s.searchParameterInfo.Code}")));
                                break;
                        }
                    }
                    else if (string.Equals(searchParam.Key, KnownQueryParameterNames.Type, StringComparison.OrdinalIgnoreCase))
                    {
                        routeValues.Add(searchParam.Key, searchParam.Value);
                    }
                    else
                    {
                        // 3. The exclude unsupported parameters
                        IEnumerable<string> removedValues = searchParamsToRemove[searchParam.Key];
                        StringValues usedValues = removedValues.Any()
                            ? new StringValues(
                                searchParam.Value.Select(x => x.SplitByOrSeparator().Except(removedValues).JoinByOrSeparator())
                                .Where(x => !string.IsNullOrEmpty(x))
                                .ToArray())
                            : searchParam.Value;

                        if (usedValues.Any())
                        {
                            routeValues.Add(searchParam.Key, usedValues);
                        }
                    }
                }
            }

            // Update continuationToken if new value is provided.
            if (continuationToken != null)
            {
                routeValues[KnownQueryParameterNames.ContinuationToken] = continuationToken;
            }

            return GetRouteUri(
                ActionContext.HttpContext,
                routeName,
                routeValues,
                Request.Scheme,
                Request.Host.Value);
        }

        public Uri ResolveRouteNameUrl(string routeName, IDictionary<string, object> routeValues)
        {
            var routeValueDictionary = new RouteValueDictionary(routeValues);

            return GetRouteUri(
                ActionContext.HttpContext,
                routeName,
                routeValueDictionary,
                Request.Scheme,
                Request.Host.Value);
        }

        public Uri ResolveOperationResultUrl(string operationName, string id)
        {
            EnsureArg.IsNotNullOrWhiteSpace(operationName, nameof(operationName));
            EnsureArg.IsNotNullOrWhiteSpace(id, nameof(id));

            string routeName = null;

            switch (operationName)
            {
                case OperationsConstants.Export:
                    routeName = RouteNames.GetExportStatusById;
                    break;
                case OperationsConstants.Reindex:
                    routeName = RouteNames.GetReindexStatusById;
                    break;
                case OperationsConstants.Import:
                    routeName = RouteNames.GetImportStatusById;
                    break;
                case OperationsConstants.PatientEverything:
                    routeName = RouteNames.PatientEverythingById;
                    break;
                case OperationsConstants.BulkDelete:
                    routeName = RouteNames.GetBulkDeleteStatusById;
                    break;
                default:
                    throw new OperationNotImplementedException(string.Format(Resources.OperationNotImplemented, operationName));
            }

            var routeValues = new RouteValueDictionary()
            {
                { KnownActionParameterNames.Id, id },
            };

            return GetRouteUri(
                ActionContext.HttpContext,
                routeName,
                routeValues,
                Request.Scheme,
                Request.Host.Value);
        }

        public Uri ResolveOperationDefinitionUrl(string operationName)
        {
            EnsureArg.IsNotNullOrWhiteSpace(operationName, nameof(operationName));

            string routeName = null;
            switch (operationName)
            {
                case OperationsConstants.Export:
                    routeName = RouteNames.ExportOperationDefinition;
                    break;
                case OperationsConstants.PatientExport:
                    routeName = RouteNames.PatientExportOperationDefinition;
                    break;
                case OperationsConstants.GroupExport:
                    routeName = RouteNames.GroupExportOperationDefinition;
                    break;
                case OperationsConstants.AnonymizedExport:
                    routeName = RouteNames.AnonymizedExportOperationDefinition;
                    break;
                case OperationsConstants.Reindex:
                    routeName = RouteNames.ReindexOperationDefintion;
                    break;
                case OperationsConstants.ResourceReindex:
                    routeName = RouteNames.ResourceReindexOperationDefinition;
                    break;
                case OperationsConstants.ConvertData:
                    routeName = RouteNames.ConvertDataOperationDefinition;
                    break;
                case OperationsConstants.MemberMatch:
                    routeName = RouteNames.MemberMatchOperationDefinition;
                    break;
                case OperationsConstants.PurgeHistory:
                    routeName = RouteNames.PurgeHistoryDefinition;
                    break;
                case OperationsConstants.BulkDelete:
                    routeName = RouteNames.BulkDeleteDefinition;
                    break;
                case OperationsConstants.SearchParameterStatus:
                    routeName = RouteNames.SearchParameterStatusOperationDefinition;
                    break;
                default:
                    throw new OperationNotImplementedException(string.Format(Resources.OperationNotImplemented, operationName));
            }

            return GetRouteUri(
                ActionContext.HttpContext,
                routeName,
                null,
                Request.Scheme,
                Request.Host.Value);
        }

        private Uri GetRouteUri(HttpContext httpContext, string routeName, RouteValueDictionary routeValues, string scheme, string host)
        {
            var uriString = string.Empty;

            if (httpContext == null)
            {
                // Keep it for UTs
                uriString = UrlHelper.RouteUrl(
                    routeName,
                    routeValues,
                    scheme,
                    host);
            }
            else
            {
                // NOTE: Append "/" to the end of the path base to workaround a known bug in UrlHelper.RouteUrl for endpoint routing.
                //       Remove the workaround when we pick up the fix. (https://github.com/dotnet/aspnetcore/issues/53177)
                var pathBase = httpContext.Request?.PathBase.ToString();
                if (!string.IsNullOrEmpty(pathBase) && !pathBase.EndsWith('/') && (string.IsNullOrEmpty(httpContext.Request?.Path) || string.Equals(httpContext.Request?.Path, "/", StringComparison.Ordinal)))
                {
                    pathBase += "/";
                }

                uriString = _linkGenerator.GetUriByRouteValues(
                    httpContext,
                    routeName,
                    routeValues,
                    scheme,
                    new HostString(host),
                    pathBase);
            }

            return new Uri(uriString);
        }
    }
}
