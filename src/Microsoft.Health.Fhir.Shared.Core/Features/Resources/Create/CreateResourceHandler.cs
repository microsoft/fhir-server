// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Create
{
    public class CreateResourceHandler : BaseConditionalHandler, IRequestHandler<CreateResourceRequest, UpsertResourceResponse>
    {
        private readonly Dictionary<string, (string resourceId, string resourceType)> _referenceIdDictionary;

        public CreateResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ISearchService searchService,
            ResourceIdProvider resourceIdProvider)
            : base(fhirDataStore, searchService, conformanceProvider, resourceWrapperFactory, resourceIdProvider)
        {
            _referenceIdDictionary = new Dictionary<string, (string resourceId, string resourceType)>();
        }

        public async Task<UpsertResourceResponse> Handle(CreateResourceRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            var resource = message.Resource.Instance.ToPoco<Resource>();

            // If an Id is supplied on create it should be removed/ignored
            resource.Id = null;

            // Added

            // await ResolveBundleReferencesAsync(resource, _referenceIdDictionary, cancellationToken);

            ResourceWrapper resourceWrapper = CreateResourceWrapper(resource, deleted: false);

            bool keepHistory = await ConformanceProvider.Value.CanKeepHistory(resource.TypeName, cancellationToken);

            UpsertOutcome result = await FhirDataStore.UpsertAsync(
                resourceWrapper,
                weakETag: null,
                allowCreate: true,
                keepHistory: keepHistory,
                cancellationToken: cancellationToken);

            resource.VersionId = result.Wrapper.Version;

            return new UpsertResourceResponse(new SaveOutcome(resource.ToResourceElement(), SaveOutcomeType.Created));
        }

        private static IEnumerable<ResourceReference> ResourceRefUrl(Resource resource)
        {
            foreach (Base child in resource.Children)
            {
                if (child is ResourceReference targetTypeObject)
                {
                    yield return targetTypeObject;
                }
            }

        }

        private async System.Threading.Tasks.Task ResolveBundleReferencesAsync(Resource resource, Dictionary<string, (string resourceId, string resourceType)> referenceIdDictionary, CancellationToken cancellationToken)
        {
            IEnumerable<ResourceReference> references = ResourceRefUrl(resource);

            foreach (ResourceReference reference in references)
            {
                if (string.IsNullOrWhiteSpace(reference.Reference))
                {
                    continue;
                }

                // Checks to see if this reference has already been assigned an Id
                if (referenceIdDictionary.TryGetValue(reference.Reference, out var referenceInformation))
                {
                    reference.Reference = $"{referenceInformation.resourceType}/{referenceInformation.resourceId}";
                }
                else
                {
                    if (reference.Reference.Contains("?", StringComparison.Ordinal))
                    {
                        string[] queries = reference.Reference.Split("?");
                        string resourceType = queries[0];
                        string conditionalQueries = queries[1];

                        if (!ModelInfoProvider.IsKnownResource(resourceType))
                        {
                            throw new RequestNotValidException(string.Format(Core.Resources.ResourceNotSupported, resourceType, reference.Reference));
                        }

                        SearchResultEntry[] results = await GetExistingResourceId(resource.ResourceType.ToString(), resourceType, conditionalQueries, cancellationToken);

                        if (results == null || results.Length != 1)
                        {
                            throw new RequestNotValidException(string.Format(Core.Resources.InvalidConditionalReference, reference.Reference));
                        }

                        string resourceId = results[0].Resource.ResourceId;

                        referenceIdDictionary.Add(reference.Reference, (resourceId, resourceType));

                        reference.Reference = $"{resourceType}/{resourceId}";
                    }
                }
            }
        }

        private async Task<SearchResultEntry[]> GetExistingResourceId(string requestUrl, string resourceType, StringValues conditionalQueries, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(resourceType) || string.IsNullOrEmpty(conditionalQueries))
            {
                throw new RequestNotValidException(string.Format(Core.Resources.InvalidConditionalReference, requestUrl));
            }

            Tuple<string, string>[] conditionalParameters = QueryHelpers.ParseQuery(conditionalQueries)
                              .SelectMany(query => query.Value, (query, value) => Tuple.Create(query.Key, value)).ToArray();

            var searchResourceRequest = new SearchResourceRequest(resourceType, conditionalParameters);

            return await Search(searchResourceRequest.ResourceType, searchResourceRequest.Queries, cancellationToken);
        }
    }
}
