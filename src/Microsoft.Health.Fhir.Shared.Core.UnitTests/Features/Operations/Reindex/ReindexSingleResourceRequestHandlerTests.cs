// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Reindex;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Reindex
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    public class ReindexSingleResourceRequestHandlerTests
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IFhirDataStore _fhirDataStore;
        private readonly ISearchIndexer _searchIndexer;
        private readonly IResourceDeserializer _resourceDeserializer;
        private readonly ReindexSingleResourceRequestHandler _reindexHandler;
        private readonly CancellationToken _cancellationToken;

        private const string HttpGetName = "GET";
        private const string HttpPostName = "POST";

        public ReindexSingleResourceRequestHandlerTests()
        {
            _authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            _fhirDataStore = Substitute.For<IFhirDataStore>();
            _searchIndexer = Substitute.For<ISearchIndexer>();
            _resourceDeserializer = Substitute.For<IResourceDeserializer>();
            _cancellationToken = CancellationToken.None;

            _authorizationService.CheckAccess(Arg.Is<DataActions>(DataActions.Reindex), Arg.Is<CancellationToken>(_cancellationToken)).Returns<DataActions>(DataActions.Reindex);

            var searchParameterOperations = Substitute.For<ISearchParameterOperations>();
            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();

            _reindexHandler = new ReindexSingleResourceRequestHandler(
                _authorizationService,
                _fhirDataStore,
                _searchIndexer,
                _resourceDeserializer,
                searchParameterOperations,
                searchParameterDefinitionManager);
        }

        [Fact]
        public async Task GivenUserDoesNotHavePermissionForReindex_WhenHandle_ThenUnauthorizedExceptionIsThrown()
        {
            _authorizationService.CheckAccess(Arg.Is(DataActions.Reindex), Arg.Any<CancellationToken>()).Returns(DataActions.None);
            var request = GetReindexRequest(HttpGetName);

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _reindexHandler.Handle(request, _cancellationToken));
        }

        [Fact]
        public async Task GivenResourceDoesNotExist_WhenHandle_ThenResourceNotFoundExceptionIsThrown()
        {
            _fhirDataStore.GetAsync(Arg.Any<ResourceKey>(), _cancellationToken).Returns(Task.FromResult<ResourceWrapper>(null));

            var request = GetReindexRequest(HttpGetName);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => _reindexHandler.Handle(request, _cancellationToken));
        }

        [Theory]
        [InlineData(HttpGetName)]
        [InlineData(HttpPostName)]
        public async Task GivenNewSearchIndicesGetRequest_WhenHandle_ThenTheirValuesArePresentInResponse(string httpMethodName)
        {
            SetupDataStoreToReturnDummyResourceWrapper();

            var searchIndex = new SearchIndexEntry(new SearchParameterInfo("newSearchParam", "newSearchParam"), new NumberSearchValue(1));
            var searchIndex2 = new SearchIndexEntry(new SearchParameterInfo("newSearchParam2", "newSearchParam2"), new StringSearchValue("paramValue"));

            var searchIndices = new List<SearchIndexEntry>() { searchIndex, searchIndex2 };

            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(searchIndices);

            var request = GetReindexRequest(httpMethodName);

            ReindexSingleResourceResponse response = await _reindexHandler.Handle(request, _cancellationToken);
            Assert.NotNull(response.ParameterResource);

            Parameters parameterResponse = response.ParameterResource.ToPoco<Parameters>();
            bool newSearchParamPresent = false;
            bool newSearchParam2Present = false;
            foreach (Parameters.ParameterComponent param in parameterResponse.Parameter)
            {
                if (param.Name == "newSearchParam")
                {
                    newSearchParamPresent = true;
                    Assert.Equal("1", param.Value.ToString());
                }

                if (param.Name == "newSearchParam2")
                {
                    newSearchParam2Present = true;
                    Assert.Equal("paramValue", param.Value.ToString());
                }
            }

            Assert.True(newSearchParamPresent);
            Assert.True(newSearchParam2Present);

            await ValidateUpdateCallBasedOnHttpMethodType(httpMethodName);
        }

        [Theory]
        [InlineData(HttpGetName)]
        [InlineData(HttpPostName)]
        public async Task GivenDuplicateNewSearchIndices_WhenHandle_ThenBothValuesArePresentInResponse(string httpMethodName)
        {
            SetupDataStoreToReturnDummyResourceWrapper();

            var searchParamInfo = new SearchParameterInfo("newSearchParam", "newSearchParam");
            var searchIndex = new SearchIndexEntry(searchParamInfo, new StringSearchValue("name1"));
            var searchIndex2 = new SearchIndexEntry(searchParamInfo, new StringSearchValue("name2"));
            var searchIndices = new List<SearchIndexEntry>() { searchIndex, searchIndex2 };

            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(searchIndices);

            var request = GetReindexRequest(httpMethodName);

            ReindexSingleResourceResponse response = await _reindexHandler.Handle(request, _cancellationToken);
            Assert.NotNull(response.ParameterResource);

            Parameters parameterResponse = response.ParameterResource.ToPoco<Parameters>();
            bool newSearchParamPresent = false;
            bool name1Present = false;
            bool name2Present = false;

            foreach (Parameters.ParameterComponent param in parameterResponse.Parameter)
            {
                if (param.Name == "newSearchParam")
                {
                    newSearchParamPresent = true;
                    if (param.Value.ToString() == "name1")
                    {
                        name1Present = true;
                    }

                    if (param.Value.ToString() == "name2")
                    {
                        name2Present = true;
                    }
                }
            }

            Assert.True(newSearchParamPresent);
            Assert.True(name1Present);
            Assert.True(name2Present);

            await ValidateUpdateCallBasedOnHttpMethodType(httpMethodName);
        }

        private void SetupDataStoreToReturnDummyResourceWrapper()
        {
            ResourceElement patientResourceElement = Samples.GetDefaultPatient();
            Patient patientResource = patientResourceElement.ToPoco<Patient>();
            RawResource rawResource = new RawResource(new FhirJsonSerializer().SerializeToString(patientResource), FhirResourceFormat.Json, isMetaSet: false);

            ResourceWrapper dummyResourceWrapper = new ResourceWrapper(
                patientResourceElement.Id,
                versionId: "1",
                patientResourceElement.InstanceType,
                rawResource,
                request: null,
                patientResourceElement.LastUpdated ?? Clock.UtcNow,
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null);

            _fhirDataStore.GetAsync(Arg.Any<ResourceKey>(), _cancellationToken).Returns(Task.FromResult(dummyResourceWrapper));
        }

        private ReindexSingleResourceRequest GetReindexRequest(string httpMethod, string resourceId = null, string resourceType = null)
        {
            resourceId = resourceId ?? Guid.NewGuid().ToString();
            resourceType = resourceType ?? "Patient";

            return new ReindexSingleResourceRequest(httpMethod, resourceType, resourceId);
        }

        private async Task ValidateUpdateCallBasedOnHttpMethodType(string httpMethodName)
        {
            if (httpMethodName == HttpPostName)
            {
                await _fhirDataStore.Received().UpdateSearchParameterIndicesAsync(Arg.Any<ResourceWrapper>(), _cancellationToken);
            }
            else if (httpMethodName == HttpGetName)
            {
                await _fhirDataStore.DidNotReceive().UpdateSearchParameterIndicesAsync(Arg.Any<ResourceWrapper>(), _cancellationToken);
            }
        }
    }
}
