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
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search;
using Microsoft.Health.Fhir.ValueSets;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public class BundleFactory : IBundleFactory
    {
        private readonly IUrlResolver _urlResolver;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;

        public BundleFactory(IUrlResolver urlResolver, RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor)
        {
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

            _urlResolver = urlResolver;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
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

        public string CreateNewSearchBundle(SearchResult searchResult)
        {
            return CreateNewBundle(searchResult, Bundle.BundleType.Searchset, r =>
            {
                var fullUri = new FhirUri(_urlResolver.ResolveResourceWrapperUrl(r.Resource));
                var search = r.SearchEntryMode == SearchEntryMode.Match ? "match" : "include";

                var newResource = new JObject();
                newResource.Add("fullUrl", fullUri.ToString());
                newResource.Add("resource", new JRaw(r.Resource.RawResource.Data));
                newResource.Add("search", new JRaw($"{{\"mode\":\"{search}\"}}"));
                return newResource;
            });
        }

        public ResourceElement CreateHistoryBundle(SearchResult result)
        {
            return CreateBundle(result, Bundle.BundleType.History, r =>
            {
                var resource = new RawBundleEntryComponent(r.Resource);
                var hasVerb = Enum.TryParse(r.Resource.Request?.Method, true, out Bundle.HTTPVerb httpVerb);

                resource.FullUrlElement = new FhirUri(_urlResolver.ResolveResourceWrapperUrl(r.Resource, true));
                resource.Request = new Bundle.RequestComponent
                {
                    Method = hasVerb ? httpVerb : null,
                    Url = hasVerb ? $"{r.Resource.ResourceTypeName}/{(httpVerb == Bundle.HTTPVerb.POST ? null : r.Resource.ResourceId)}" : null,
                };

                string statusString;
                switch (httpVerb)
                {
                    case Bundle.HTTPVerb.POST:
                        statusString = ((int)HttpStatusCode.Created).ToString() + " " + HttpStatusCode.Created;
                        break;
                    case Bundle.HTTPVerb.PUT:
                    case Bundle.HTTPVerb.GET:
                        statusString = ((int)HttpStatusCode.OK).ToString() + " " + HttpStatusCode.OK;
                        break;
                    case Bundle.HTTPVerb.DELETE:
                        statusString = ((int)HttpStatusCode.NoContent).ToString() + " " + HttpStatusCode.NoContent;
                        break;
                    default:
                        throw new NotImplementedException();
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

        private void CreateLinks(SearchResult result, JObject bundle)
        {
            bool problemWithLinks = false;
            var links = new JArray();
            if (result.ContinuationToken != null)
            {
                try
                {
                    var nextLink = _urlResolver.ResolveRouteUrl(
                        result.UnsupportedSearchParameters,
                        result.SortOrder,
                        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(result.ContinuationToken)),
                        true);

                    links.Add(JToken.Parse($"{{\"relation\":\"next\", \"url\":\"{nextLink}\"}}"));
                }
                catch (UriFormatException)
                {
                    problemWithLinks = true;
                }
            }

            try
            {
                // Add the self link to indicate which search parameters were used.
                var selfLink = _urlResolver.ResolveRouteUrl(result.UnsupportedSearchParameters, result.SortOrder);
                links.Add(JToken.Parse($"{{\"relation\":\"self\", \"url\":\"{selfLink}\"}}"));
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

            bundle.Add("link", links);
        }

        private string CreateNewBundle(SearchResult result, Bundle.BundleType type, Func<SearchResultEntry, JToken> selectionFunction)
        {
            EnsureArg.IsNotNull(result, nameof(result));

            // Create the bundle from the result.
            var bundle = new JObject();
            bundle.Add("resourceType", "Bundle");
            bundle.Add("id", _fhirRequestContextAccessor.RequestContext.CorrelationId);
            bundle.Add("type", type.ToString());
            bundle.Add("meta", JToken.Parse($"{{\"lastUpdated\":\"{Clock.UtcNow.ToInstantString()}\"}}"));

            CreateLinks(result, bundle);

            JArray entries = new JArray(result.Results.Select(selectionFunction));

            bundle.Add("entry", entries);

            return bundle.ToString();
        }

        private ResourceElement CreateBundle(SearchResult result, Bundle.BundleType type, Func<SearchResultEntry, Bundle.EntryComponent> selectionFunction)
        {
            EnsureArg.IsNotNull(result, nameof(result));

            // Create the bundle from the result.
            var bundle = new Bundle();

            // CreateLinks(result, bundle);

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
