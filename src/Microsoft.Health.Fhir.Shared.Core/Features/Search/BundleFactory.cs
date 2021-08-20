// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public class BundleFactory : IBundleFactory
    {
        private readonly IUrlResolver _urlResolver;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly ILogger<BundleFactory> _logger;

        public BundleFactory(IUrlResolver urlResolver, RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor, ILogger<BundleFactory> logger)
        {
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _urlResolver = urlResolver;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _logger = logger;
        }

        public ResourceElement CreateSearchBundle(SearchResult result)
        {
            return CreateBundle(result, Bundle.BundleType.Searchset, r =>
            {
                var resource = new RawBundleEntryComponent(r.Resource);

                resource.FullUrlElement = new FhirUri(_urlResolver.ResolveResourceWrapperUrl(r.Resource));
                resource.Search = new Bundle.SearchComponent
                {
                    Mode = r.SearchEntryMode == SearchEntryMode.Match ? Bundle.SearchEntryMode.Match : Bundle.SearchEntryMode.Include,
                };

                return resource;
            });
        }

        public ResourceElement CreateHistoryBundle(SearchResult result)
        {
            return CreateBundle(result, Bundle.BundleType.History, r =>
            {
                var resource = new RawBundleEntryComponent(r.Resource);

                var hasVerb = Enum.TryParse(r.Resource.Request?.Method, true, out Bundle.HTTPVerb httpVerb);
#if Stu3
                // STU3 doesn't have PATCH verb, so let's map it PUT.
                if (!hasVerb && string.Equals("PATCH", r.Resource.Request?.Method, StringComparison.OrdinalIgnoreCase))
                {
                    hasVerb = true;
                    httpVerb = Bundle.HTTPVerb.PUT;
                }
#endif
                resource.FullUrlElement = new FhirUri(_urlResolver.ResolveResourceWrapperUrl(r.Resource, true));
                if (hasVerb)
                {
                    resource.Request = new Bundle.RequestComponent
                    {
                        Method = httpVerb,
                        Url = $"{r.Resource.ResourceTypeName}/{(httpVerb == Bundle.HTTPVerb.POST ? null : r.Resource.ResourceId)}",
                    };
                }

                string statusString = null;

                string ToStatusString(HttpStatusCode statusCode)
                {
                    return $"{(int)statusCode} {statusCode}";
                }

                switch (httpVerb)
                {
                    case Bundle.HTTPVerb.POST:
                        statusString = ToStatusString(HttpStatusCode.Created);
                        break;
                    case Bundle.HTTPVerb.PUT:

                        if (string.Equals(r.Resource.Version, "1", StringComparison.Ordinal))
                        {
                            statusString = ToStatusString(HttpStatusCode.Created);
                            break;
                        }

                        statusString = ToStatusString(HttpStatusCode.OK);
                        break;

                    case Bundle.HTTPVerb.GET:
#if !Stu3
                    case Bundle.HTTPVerb.PATCH:
                    case Bundle.HTTPVerb.HEAD:
#endif
                        statusString = ToStatusString(HttpStatusCode.OK);
                        break;
                    case Bundle.HTTPVerb.DELETE:
                        statusString = ToStatusString(HttpStatusCode.NoContent);
                        break;
                    default:
                        throw new NotImplementedException($"{httpVerb} was not defined.");
                }

                resource.Response = new Bundle.ResponseComponent
                {
                    Status = statusString,
                    LastModified = r.Resource.LastModified,
                    Etag = WeakETag.FromVersionId(r.Resource.Version).ToString(),
                };

                return resource;
            });
        }

        private void CreateLinks(SearchResult result, Bundle bundle)
        {
            bool problemWithLinks = false;
            if (result.ContinuationToken != null)
            {
                try
                {
                    bundle.NextLink = _urlResolver.ResolveRouteUrl(
                        result.UnsupportedSearchParameters,
                        result.SortOrder,
                        ContinuationTokenConverter.Encode(result.ContinuationToken),
                        true);
                }
                catch (UriFormatException)
                {
                    problemWithLinks = true;
                }
            }

            try
            {
                // Add the self link to indicate which search parameters were used.
                bundle.SelfLink = _urlResolver.ResolveRouteUrl(result.UnsupportedSearchParameters, result.SortOrder);
            }
            catch (UriFormatException)
            {
                problemWithLinks = true;
            }

            if (problemWithLinks)
            {
                _fhirRequestContextAccessor.RequestContext.BundleIssues.Add(
                          new OperationOutcomeIssue(
                              OperationOutcomeConstants.IssueSeverity.Warning,
                              OperationOutcomeConstants.IssueType.NotSupported,
                              string.Format(Core.Resources.LinksCantBeCreated)));
            }
        }

        private ResourceElement CreateBundle(SearchResult result, Bundle.BundleType type, Func<SearchResultEntry, Bundle.EntryComponent> selectionFunction)
        {
            EnsureArg.IsNotNull(result, nameof(result));

            // Create the bundle from the result.
            var bundle = new Bundle();
            CreateLinks(result, bundle);

            if (_fhirRequestContextAccessor.RequestContext.BundleIssues.Any())
            {
                var operationOutcome = new OperationOutcome
                {
                    Id = _fhirRequestContextAccessor.RequestContext.CorrelationId,
                    Issue = new List<OperationOutcome.IssueComponent>(_fhirRequestContextAccessor.RequestContext.BundleIssues.Select(x => x.ToPoco())),
                };

                bundle.Entry.Add(new Bundle.EntryComponent
                {
                    Resource = operationOutcome,
                    Search = new Bundle.SearchComponent
                    {
                        Mode = Bundle.SearchEntryMode.Outcome,
                    },
                });
            }

            IEnumerable<Bundle.EntryComponent> entries = result.Results.Select(selectionFunction);

            bundle.Entry.AddRange(entries);
            bundle.Id = _fhirRequestContextAccessor.RequestContext.CorrelationId;
            bundle.Type = type;
            bundle.Total = result?.TotalCount;
            bundle.Meta = new Meta
            {
                LastUpdated = Clock.UtcNow,
            };

            return bundle.ToResourceElement();
        }
    }
}
