// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Core;
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
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly ResourceDeserializer _deserializer;

        public BundleFactory(IUrlResolver urlResolver, IFhirRequestContextAccessor fhirRequestContextAccessor, ResourceDeserializer deserializer)
        {
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(deserializer, nameof(deserializer));

            _urlResolver = urlResolver;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _deserializer = deserializer;
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

                resource.FullUrlElement = new FhirUri(_urlResolver.ResolveResourceWrapperUrl(resource.Wrapper, true));
                resource.Request = new Bundle.RequestComponent
                {
                    Method = hasVerb ? (Bundle.HTTPVerb?)httpVerb : null,
                    Url = hasVerb ? $"{resource.Wrapper.ResourceTypeName}/{(httpVerb == Bundle.HTTPVerb.POST ? null : resource.Wrapper.ResourceId)}" : null,
                };
                resource.Response = new Bundle.ResponseComponent
                {
                    LastModified = r.Resource.LastModified,
                    Etag = WeakETag.FromVersionId(r.Resource.Version).ToString(),
                };

                return resource;
            });
        }

        private ResourceElement CreateBundle(SearchResult result, Bundle.BundleType type, Func<SearchResultEntry, Bundle.EntryComponent> selectionFunction)
        {
            EnsureArg.IsNotNull(result, nameof(result));

            // Create the bundle from the result.
            var bundle = new Bundle();

            if (result != null)
            {
                IEnumerable<Bundle.EntryComponent> entries = result.Results.Select(selectionFunction);

                bundle.Entry.AddRange(entries);

                if (result.ContinuationToken != null)
                {
                    bundle.NextLink = _urlResolver.ResolveRouteUrl(
                        result.UnsupportedSearchParameters,
                        result.UnsupportedSortingParameters,
                        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(result.ContinuationToken)),
                        true);
                }
            }

            // Add the self link to indicate which search parameters were used.
            bundle.SelfLink = _urlResolver.ResolveRouteUrl(result.UnsupportedSearchParameters, result.UnsupportedSortingParameters);

            bundle.Id = _fhirRequestContextAccessor.FhirRequestContext.CorrelationId;
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
