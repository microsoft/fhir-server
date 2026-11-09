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
using Hl7.Fhir.Model;
using Medino;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
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

        /// <summary>
        /// The name of the parameter that holds warnings, as an OperationOutcome, in the response.
        /// </summary>
        internal const string IssuesParameterName = "Issues";

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

        public async Task<ReindexSingleResourceResponse> HandleAsync(ReindexSingleResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            await _authorizationService.CheckAccess(
                DataActions.Reindex | DataActions.Read | DataActions.ReadById,
                grantedActions =>
                    (grantedActions & DataActions.Reindex) == DataActions.Reindex &&
                    (grantedActions & (DataActions.Read | DataActions.ReadById)) != DataActions.None,
                true,
                cancellationToken);

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
            IReadOnlyList<string> warnings = null;
            if (request.HttpMethod == HttpPostName)
            {
                (newIndices, warnings) = await ProcessPostReindexSingleResourceRequest(storedResource, newIndices);
            }

            // Create a new parameter resource and include the new search indices and the corresponding values.
            var parametersResource = new Hl7.Fhir.Model.Parameters
            {
                Id = Guid.NewGuid().ToString(),
                VersionId = "1",
                Parameter = new List<Hl7.Fhir.Model.Parameters.ParameterComponent>(),
            };

            foreach (SearchIndexEntry searchIndex in newIndices)
            {
                parametersResource.Parameter.Add(new Hl7.Fhir.Model.Parameters.ParameterComponent() { Name = searchIndex.SearchParameter.Code.ToString(), Value = new FhirString(searchIndex.Value.ToString()) });
            }

            var issues = new List<OperationOutcome.IssueComponent>();

            // Search values the data store couldn't index (for example URI values it can't tell apart) are not stored.
            issues.AddRange((warnings ?? Array.Empty<string>()).Select(warning => new OperationOutcome.IssueComponent
            {
                Severity = OperationOutcome.IssueSeverity.Warning,
                Code = OperationOutcome.IssueType.Informational,
                Diagnostics = warning,
            }));

            // Overlong reference ids are indexed truncated, so tell the caller which search parameters are affected.
            var longReferenceIdIssues = new List<OperationOutcomeIssue>();
            foreach ((SearchIndexEntry entry, ReferenceSearchValue reference) in LongReferenceIdIssue.GetOverlongReferences(newIndices))
            {
                LongReferenceIdIssue.TryAdd(longReferenceIdIssues, entry.SearchParameter.Code, reference.MaxResourceIdLength.Value);
            }

            issues.AddRange(longReferenceIdIssues.Select(issue => issue.ToPoco()));

            if (issues.Count > 0)
            {
                parametersResource.Parameter.Add(new Hl7.Fhir.Model.Parameters.ParameterComponent { Name = IssuesParameterName, Resource = new OperationOutcome { Issue = issues } });
            }

            return new ReindexSingleResourceResponse(parametersResource.ToResourceElement());
        }

        private async Task<(IReadOnlyCollection<SearchIndexEntry> StoredIndices, IReadOnlyList<string> Warnings)> ProcessPostReindexSingleResourceRequest(ResourceWrapper originalResource, IReadOnlyCollection<SearchIndexEntry> searchIndices)
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

            // The data store can leave out values it can't index; it removes them from the wrapper and returns warnings.
            IReadOnlyList<string> warnings = await _fhirDataStore.BulkUpdateSearchParameterIndicesAsync(new[] { updatedResource }, CancellationToken.None);
            return (updatedResource.SearchIndices ?? searchIndices, warnings);
        }
    }
}
