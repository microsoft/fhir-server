// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;

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

        public Bundle CreateSearchBundle(IEnumerable<Tuple<string, string>> unsupportedSearchParameters, SearchResult result)
        {
            // Create the bundle from the result.
            var bundle = new Bundle();

            if (result != null)
            {
                IEnumerable<Bundle.EntryComponent> entries = result.Results.Select(r =>
                {
                    Resource resource = _deserializer.Deserialize(r);

                    return new Bundle.EntryComponent
                    {
                        FullUrlElement = new FhirUri(_urlResolver.ResolveResourceUrl(resource)),
                        Resource = resource,
                        Search = new Bundle.SearchComponent
                        {
                            // TODO: For now, everything returned is a match. We will need to
                            // distinct between match and include once we support it.
                            Mode = Bundle.SearchEntryMode.Match,
                        },
                    };
                });

                bundle.Entry.AddRange(entries);

                if (result.ContinuationToken != null)
                {
                    bundle.NextLink = _urlResolver.ResolveRouteUrl(
                        unsupportedSearchParameters,
                        result.ContinuationToken);
                }
            }

            // Add the self link to indicate which search parameters were used.
            bundle.SelfLink = _urlResolver.ResolveRouteUrl(unsupportedSearchParameters);

            bundle.Id = _fhirRequestContextAccessor.FhirRequestContext.CorrelationId;
            bundle.Type = Bundle.BundleType.Searchset;
            bundle.Total = result?.TotalCount;
            bundle.Meta = new Meta
            {
                LastUpdated = Clock.UtcNow,
            };

            return bundle;
        }

        public Bundle CreateHistoryBundle(IEnumerable<Tuple<string, string>> unsupportedSearchParameters, SearchResult result)
        {
            // Create the bundle from the result.
            var bundle = new Bundle();

            if (result != null)
            {
                IEnumerable<Bundle.EntryComponent> entries = result.Results.Select(r =>
                {
                    Resource resource = _deserializer.Deserialize(r);
                    var hasVerb = Enum.TryParse(r.Request?.Method, true, out Bundle.HTTPVerb httpVerb);

                    return new Bundle.EntryComponent
                    {
                        FullUrlElement = new FhirUri(_urlResolver.ResolveResourceUrl(resource, true)),
                        Resource = resource,
                        Request = new Bundle.RequestComponent
                        {
                            Method = hasVerb ? (Bundle.HTTPVerb?)httpVerb : null,
                            Url = r.Request?.Url?.ToString(),
                        },
                        Response = new Bundle.ResponseComponent
                        {
                            LastModified = r.LastModified,
                            Etag = WeakETag.FromVersionId(r.Version).ToString(),
                        },
                    };
                });

                bundle.Entry.AddRange(entries);

                if (result.ContinuationToken != null)
                {
                    bundle.NextLink = _urlResolver.ResolveRouteUrl(
                        unsupportedSearchParameters,
                        result.ContinuationToken);
                }
            }

            // Add the self link to indicate which search parameters were used.
            bundle.SelfLink = _urlResolver.ResolveRouteUrl(unsupportedSearchParameters);

            bundle.Id = _fhirRequestContextAccessor.FhirRequestContext.CorrelationId;
            bundle.Type = Bundle.BundleType.History;
            bundle.Total = result?.TotalCount;
            bundle.Meta = new Meta
            {
                LastUpdated = Clock.UtcNow,
            };

            return bundle;
        }
    }
}
