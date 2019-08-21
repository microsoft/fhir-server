// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Models;

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
                ResourceElement resource = _deserializer.Deserialize(r);

                return new Bundle.EntryComponent
                {
                    FullUrlElement = new FhirUri(_urlResolver.ResolveResourceUrl(resource)),
                    Resource = resource.Instance.ToPoco<Resource>(),
                    Search = new Bundle.SearchComponent
                    {
                        // TODO: For now, everything returned is a match. We will need to
                        // distinct between match and include once we support it.
                        Mode = Bundle.SearchEntryMode.Match,
                    },
                };
            });
        }

        public ResourceElement CreateHistoryBundle(SearchResult result)
        {
            return CreateBundle(result, Bundle.BundleType.History, r =>
            {
                var resource = _deserializer.Deserialize(r);
                var hasVerb = Enum.TryParse(r.Request?.Method, true, out Bundle.HTTPVerb httpVerb);

                return new Bundle.EntryComponent
                {
                    FullUrlElement = new FhirUri(_urlResolver.ResolveResourceUrl(resource, true)),
                    Resource = resource.Instance.ToPoco<Resource>(),
                    Request = new Bundle.RequestComponent
                    {
                        Method = hasVerb ? (Bundle.HTTPVerb?)httpVerb : null,
                        Url = hasVerb ? $"{resource.InstanceType}/{(httpVerb == Bundle.HTTPVerb.POST ? null : resource.Id)}" : null,
                    },
                    Response = new Bundle.ResponseComponent
                    {
                        LastModified = r.LastModified,
                        Etag = WeakETag.FromVersionId(r.Version).ToString(),
                    },
                };
            });
        }

        private ResourceElement CreateBundle(SearchResult result, Bundle.BundleType type, Func<ResourceWrapper, Bundle.EntryComponent> selectionFunction)
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
                        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(result.ContinuationToken)));
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
