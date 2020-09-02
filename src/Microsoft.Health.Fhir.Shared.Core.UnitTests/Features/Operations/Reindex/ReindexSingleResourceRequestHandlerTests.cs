// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Reindex;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.Reindex
{
    public class ReindexSingleResourceRequestHandlerTests
    {
        private readonly IFhirAuthorizationService _authorizationService;
        private readonly IFhirDataStore _fhirDataStore;
        private readonly ISearchIndexer _searchIndexer;
        private readonly IResourceDeserializer _resourceDeserializer;
        private readonly ReindexSingleResourceRequestHandler _reindexHandler;

        private readonly CancellationToken _cancellationToken;

        public ReindexSingleResourceRequestHandlerTests()
        {
            _authorizationService = Substitute.For<IFhirAuthorizationService>();
            _fhirDataStore = Substitute.For<IFhirDataStore>();
            _searchIndexer = Substitute.For<ISearchIndexer>();
            _resourceDeserializer = Substitute.For<IResourceDeserializer>();
            _cancellationToken = CancellationToken.None;

            _authorizationService.CheckAccess(Arg.Is(DataActions.Reindex)).Returns(DataActions.Reindex);

            _reindexHandler = new ReindexSingleResourceRequestHandler(
                _authorizationService,
                _fhirDataStore,
                _searchIndexer,
                _resourceDeserializer);
        }

        [Fact]
        public async Task GivenUserDoesNotHavePermissionForReindex_WhenHandle_ThenUnauthorizedExceptionIsThrown()
        {
            _authorizationService.CheckAccess(Arg.Is(DataActions.Reindex)).Returns(DataActions.None);
            var request = GetReindexRequest();

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _reindexHandler.Handle(request, _cancellationToken));
        }

        [Fact]
        public async Task GivenResourceDoesNotExist_WhenHandle_ThenResourceNotFoundExceptionIsThrown()
        {
            _fhirDataStore.GetAsync(Arg.Any<ResourceKey>(), _cancellationToken).Returns(Task.FromResult<ResourceWrapper>(null));

            var request = GetReindexRequest();
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => _reindexHandler.Handle(request, _cancellationToken));
        }

        [Fact]
        public async Task GivenNewSearchIndices_WhenHandle_ThenTheyArePresentInResponse()
        {
            SetupDataStoreToReturnDummyResourceWrapper();

            var searchIndex = new SearchIndexEntry(new SearchParameterInfo("newSearchParam"), new NumberSearchValue(1));
            var searchIndex2 = new SearchIndexEntry(new SearchParameterInfo("newSearchParam2"), new StringSearchValue("paramValue"));

            var searchIndices = new List<SearchIndexEntry>() { searchIndex, searchIndex2 };

            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(searchIndices);

            var request = GetReindexRequest();

            ReindexSingleResourceResponse response = await _reindexHandler.Handle(request, _cancellationToken);
            Assert.NotNull(response.ParameterResource);

            Parameters parameterResponse = response.ParameterResource.ToPoco<Parameters>();
            bool newSearchIndicesPresent = false;
            foreach (Parameters.ParameterComponent param in parameterResponse.Parameter)
            {
                if (param.Name == "newSearchIndices")
                {
                    newSearchIndicesPresent = true;
                    Assert.Contains("newSearchParam", param.Value.ToString());
                    Assert.Contains("newSearchParam2", param.Value.ToString());
                }
            }

            Assert.True(newSearchIndicesPresent);
        }

        [Fact]
        public async Task GivenDuplicateNewSearchIndices_WhenHandle_ThenOnlyOneInstanceIsPresentInResponse()
        {
            SetupDataStoreToReturnDummyResourceWrapper();

            var searchParamInfo = new SearchParameterInfo("newSearchParam");
            var searchIndex = new SearchIndexEntry(searchParamInfo, new StringSearchValue("name1"));
            var searchIndex2 = new SearchIndexEntry(searchParamInfo, new StringSearchValue("name2"));
            var searchIndices = new List<SearchIndexEntry>() { searchIndex, searchIndex2 };

            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(searchIndices);

            var request = GetReindexRequest();

            ReindexSingleResourceResponse response = await _reindexHandler.Handle(request, _cancellationToken);
            Assert.NotNull(response.ParameterResource);

            Parameters parameterResponse = response.ParameterResource.ToPoco<Parameters>();
            bool newSearchIndicesPresent = false;
            foreach (Parameters.ParameterComponent param in parameterResponse.Parameter)
            {
                if (param.Name == "newSearchIndices")
                {
                    newSearchIndicesPresent = true;
                    Assert.Contains("newSearchParam", param.Value.ToString());

                    // Validate that there is only one instance present.
                    string paramValue = param.Value.ToString();
                    int index = paramValue.IndexOf("newSearchParam");
                    Assert.True(index >= 0);

                    index = paramValue.IndexOf("newSearchParam", index + 1);
                    Assert.True(index == -1);
                }
            }

            Assert.True(newSearchIndicesPresent);
        }

        private void SetupDataStoreToReturnDummyResourceWrapper()
        {
            ResourceElement patientResourceElement = Samples.GetDefaultPatient();
            Patient patientResource = patientResourceElement.ToPoco<Patient>();
            RawResource rawResource = new RawResource(new FhirJsonSerializer().SerializeToString(patientResource), FhirResourceFormat.Json);

            ResourceWrapper dummyResourceWrapper = new ResourceWrapper(
                patientResourceElement,
                rawResource,
                request: null,
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null);

            _fhirDataStore.GetAsync(Arg.Any<ResourceKey>(), _cancellationToken).Returns(Task.FromResult(dummyResourceWrapper));
        }

        private ReindexSingleResourceRequest GetReindexRequest(string resourceId = null, string resourceType = null)
        {
            resourceId = resourceId ?? Guid.NewGuid().ToString();
            resourceType = resourceType ?? "Patient";

            return new ReindexSingleResourceRequest(resourceType, resourceId);
        }
    }
}
