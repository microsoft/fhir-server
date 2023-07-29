// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Reindex;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexSingleResourceRequestHandler : IRequestHandler<ReindexSingleResourceRequest, ReindexSingleResourceResponse>
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IFhirDataStore _fhirDataStore;
        private readonly ISearchIndexer _searchIndexer;
        private readonly IResourceDeserializer _resourceDeserializer;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        private const string HttpPostName = "POST";

        public ReindexSingleResourceRequestHandler(
            IAuthorizationService<DataActions> authorizationService,
            IFhirDataStore fhirDataStore,
            ISearchIndexer searchIndexer,
            IResourceDeserializer deserializer,
            ISearchParameterOperations searchParameterOperations,
            ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(searchIndexer, nameof(searchIndexer));
            EnsureArg.IsNotNull(deserializer, nameof(deserializer));
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            _authorizationService = authorizationService;
            _fhirDataStore = fhirDataStore;
            _searchIndexer = searchIndexer;
            _resourceDeserializer = deserializer;
            _searchParameterOperations = searchParameterOperations;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
        }

        public async Task<ReindexSingleResourceResponse> Handle(ReindexSingleResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Reindex, cancellationToken) != DataActions.Reindex)
            {
                throw new UnauthorizedFhirActionException();
            }

            var key = new ResourceKey(request.ResourceType, request.ResourceId);
            ResourceWrapper storedResource = await _fhirDataStore.GetAsync(key, cancellationToken);

            if (storedResource == null)
            {
                throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundById, request.ResourceType, request.ResourceId));
            }

            await _searchParameterOperations.GetAndApplySearchParameterUpdates(cancellationToken);

            // We need to extract the "new" search indices since the assumption is that
            // a new search parameter has been added to the fhir server.
            ResourceElement resourceElement = _resourceDeserializer.Deserialize(storedResource);
            IReadOnlyCollection<SearchIndexEntry> newIndices = _searchIndexer.Extract(resourceElement);

            // If it's a post request we need to go update the resource in the database.
            if (request.HttpMethod == HttpPostName)
            {
                await ProcessPostReindexSingleResourceRequest(storedResource, newIndices);
            }

            // Create a new parameter resource and include the new search indices and the corresponding values.
            var parametersResource = new Parameters
            {
                Id = Guid.NewGuid().ToString(),
                VersionId = "1",
                Parameter = new List<Parameters.ParameterComponent>(),
            };

            foreach (SearchIndexEntry searchIndex in newIndices)
            {
                parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = searchIndex.SearchParameter.Code.ToString(), Value = new FhirString(searchIndex.Value.ToString()) });
            }

            return new ReindexSingleResourceResponse(parametersResource.ToResourceElement());
        }

        private async System.Threading.Tasks.Task ProcessPostReindexSingleResourceRequest(ResourceWrapper originalResource, IReadOnlyCollection<SearchIndexEntry> searchIndices)
        {
            var hashValue = _searchParameterDefinitionManager.GetSearchParameterHashForResourceType(originalResource.ResourceTypeName);
            ResourceWrapper updatedResource = new ResourceWrapper(
                originalResource.ResourceId,
                originalResource.Version,
                originalResource.ResourceTypeName,
                originalResource.RawResource,
                originalResource.Request,
                originalResource.LastModified,
                deleted: false,
                searchIndices,
                originalResource.CompartmentIndices,
                originalResource.LastModifiedClaims,
                hashValue,
                originalResource.ResourceSurrogateId);

            await _fhirDataStore.UpdateSearchParameterIndicesAsync(updatedResource, WeakETag.FromVersionId(originalResource.Version), CancellationToken.None);
        }
    }
}
