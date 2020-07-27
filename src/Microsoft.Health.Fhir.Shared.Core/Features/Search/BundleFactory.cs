// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using EnsureThat;
using Hl7.Fhir.ElementModel;
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
                ResourceElement resource = _deserializer.Deserialize(r.Resource);

                return new Bundle.EntryComponent
                {
                    FullUrlElement = new FhirUri(_urlResolver.ResolveResourceUrl(resource)),
                    Resource = resource.ToPoco<Resource>(),
                    Search = new Bundle.SearchComponent
                    {
                        Mode = r.SearchEntryMode == SearchEntryMode.Match ? Bundle.SearchEntryMode.Match : Bundle.SearchEntryMode.Include,
                    },
                };
            });
        }

        public ResourceElement CreateRawSearchBundle(SearchResult result)
        {
            var bundle = new Bundle()
            {
                Id = Guid.NewGuid().ToString(),
            };

            bundle.SelfLink = _urlResolver.ResolveRouteUrl(result.UnsupportedSearchParameters, result.UnsupportedSortingParameters);

            bundle.Id = _fhirRequestContextAccessor.FhirRequestContext.CorrelationId;
            bundle.Type = Bundle.BundleType.Searchset;
            bundle.Total = result?.TotalCount;
            bundle.Meta = new Meta
            {
                LastUpdated = Clock.UtcNow,
            };

            var output = result.Results.Select(x =>
            {
                var output = new RawFhirResource();
                output.FullUrlElement = new FhirUri(_urlResolver.ResolveRawResourceUrl(x.Resource));
                output.Search = new Bundle.SearchComponent
                {
                    Mode = x.SearchEntryMode == SearchEntryMode.Match ? Bundle.SearchEntryMode.Match : Bundle.SearchEntryMode.Include,
                };
                using (var ms = new MemoryStream())
                {
                    using (Utf8JsonWriter writer = new Utf8JsonWriter(ms))
                    {
                        var jsonDocument = JsonDocument.Parse(x.Resource.RawResource.Data);
                        writer.WriteStartObject();
                        bool foundMeta = false;
                        foreach (var current in jsonDocument.RootElement.EnumerateObject())
                        {
                            if (current.Name == "meta")
                            {
                                foundMeta = true;
                                foreach (var entry in current.Value.EnumerateObject())
                                {
                                    if (entry.Name == "lastUpdated")
                                    {
                                        writer.WriteString("lastUpdated", x.Resource.LastModified);
                                    }
                                    else if (entry.Name == "versionId")
                                    {
                                        writer.WriteString("versionId", x.Resource.Version);
                                    }
                                    else
                                    {
                                        entry.WriteTo(writer);
                                    }
                                }
                            }
                            else
                            {
                                // write
                                current.WriteTo(writer);
                            }
                        }

                        if (!foundMeta)
                        {
                            writer.WriteStartObject("meta");
                            writer.WriteString("lastUpdated", x.Resource.LastModified);
                            writer.WriteString("versionId", x.Resource.Version);
                            writer.WriteEndObject();
                        }

                        writer.WriteEndObject();
                        output.Content = jsonDocument;
                    }

                    using (var sr = new StreamReader(ms))
                    {
                        ms.Position = 0;
                        x.Resource.RawResource.Data = sr.ReadToEnd();
                    }
                }

                return output;
            });

            bundle.Entry.AddRange(output);
            var rebundle = bundle.ToResourceElement();

            return rebundle;
        }

        public ResourceElement CreateHistoryBundle(SearchResult result)
        {
            return CreateBundle(result, Bundle.BundleType.History, r =>
            {
                var resource = _deserializer.Deserialize(r.Resource);
                var hasVerb = Enum.TryParse(r.Resource.Request?.Method, true, out Bundle.HTTPVerb httpVerb);

                return new Bundle.EntryComponent
                {
                    FullUrlElement = new FhirUri(_urlResolver.ResolveResourceUrl(resource, true)),
                    Resource = resource.ToPoco<Resource>(),
                    Request = new Bundle.RequestComponent
                    {
                        Method = hasVerb ? (Bundle.HTTPVerb?)httpVerb : null,
                        Url = hasVerb ? $"{resource.InstanceType}/{(httpVerb == Bundle.HTTPVerb.POST ? null : resource.Id)}" : null,
                    },
                    Response = new Bundle.ResponseComponent
                    {
                        LastModified = r.Resource.LastModified,
                        Etag = WeakETag.FromVersionId(r.Resource.Version).ToString(),
                    },
                };
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
