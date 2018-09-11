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
        private readonly IFhirContextAccessor _fhirContextAccessor;

        public BundleFactory(IUrlResolver urlResolver, IFhirContextAccessor fhirContextAccessor)
        {
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(fhirContextAccessor, nameof(fhirContextAccessor));

            _urlResolver = urlResolver;
            _fhirContextAccessor = fhirContextAccessor;
        }

        public Bundle CreateSearchBundle(IEnumerable<Tuple<string, string>> unsupportedSearchParams, SearchResult result)
        {
            // Create the bundle from the result.
            var bundle = new Bundle();

            if (result != null)
            {
                IEnumerable<Bundle.EntryComponent> entries = result.Results.Select(r =>
                {
                    Resource resource = ResourceDeserializer.Deserialize(r);

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
                    bundle.NextLink = _urlResolver.ResolveSearchUrl(
                        unsupportedSearchParams,
                        result.ContinuationToken);
                }
            }

            // Add the self link to indicate which search parameters were used.
            bundle.SelfLink = _urlResolver.ResolveSearchUrl(unsupportedSearchParams);

            bundle.Id = _fhirContextAccessor.FhirContext.CorrelationId;
            bundle.Type = Bundle.BundleType.Searchset;
            bundle.Total = result?.TotalCount;
            bundle.Meta = new Meta
            {
                LastUpdated = Clock.UtcNow,
            };

            return bundle;
        }

        public Bundle CreateHistoryBundle(IEnumerable<Tuple<string, string>> unsupportedSearchParams, SearchResult result)
        {
            // Create the bundle from the result.
            var bundle = new Bundle();

            if (result != null)
            {
                IEnumerable<Bundle.EntryComponent> entries = result.Results.Select(r =>
                {
                    Resource resource = ResourceDeserializer.Deserialize(r);
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
                        _fhirContextAccessor.FhirContext.RouteName,
                        unsupportedSearchParams,
                        result.ContinuationToken);
                }
            }

            // Add the self link to indicate which search parameters were used.
            bundle.SelfLink = _urlResolver.ResolveRouteUrl(_fhirContextAccessor.FhirContext.RouteName, unsupportedSearchParams);

            bundle.Id = _fhirContextAccessor.FhirContext.CorrelationId;
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
