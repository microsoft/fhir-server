// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;
using Microsoft.Health.Core.Features.Context;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using System.Linq;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Smart
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.All)]

    public class SmartSearchTests : IClassFixture<FhirStorageTestsFixture>, IAsyncLifetime
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly IFhirStorageTestHelper _testHelper;
        private IFhirOperationDataStore _fhirOperationDataStore;
        private IScoped<IFhirDataStore> _scopedDataStore;
        private IFhirStorageTestHelper _fhirStorageTestHelper;
        private SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private ITypedElementToSearchValueConverterManager _typedElementToSearchValueConverterManager;
        private ISearchIndexer _searchIndexer;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
        private ISupportedSearchParameterDefinitionManager _supportedSearchParameterDefinitionManager;
        private SearchParameterStatusManager _searchParameterStatusManager;

        private IScoped<ISearchService> _searchService;

        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private readonly IDataStoreSearchParameterValidator _dataStoreSearchParameterValidator = Substitute.For<IDataStoreSearchParameterValidator>();

        public SmartSearchTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _testHelper = _fixture.TestHelper;
        }

        public async Task InitializeAsync()
        {
            _dataStoreSearchParameterValidator.ValidateSearchParameter(default, out Arg.Any<string>()).ReturnsForAnyArgs(x =>
            {
                x[1] = null;
                return true;
            });

            _searchParameterSupportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>()).Returns((true, false));

            _fhirOperationDataStore = _fixture.OperationDataStore;
            _fhirStorageTestHelper = _fixture.TestHelper;
            _scopedDataStore = _fixture.DataStore.CreateMockScope();

            _searchParameterDefinitionManager = _fixture.SearchParameterDefinitionManager;
            _supportedSearchParameterDefinitionManager = _fixture.SupportedSearchParameterDefinitionManager;

            _typedElementToSearchValueConverterManager = await CreateFhirTypedElementToSearchValueConverterManagerAsync();

            _searchIndexer = new TypedElementSearchIndexer(
                _supportedSearchParameterDefinitionManager,
                _typedElementToSearchValueConverterManager,
                Substitute.For<IReferenceToElementResolver>(),
                ModelInfoProvider.Instance,
                NullLogger<TypedElementSearchIndexer>.Instance);

            ResourceWrapperFactory wrapperFactory = Mock.TypeWithArguments<ResourceWrapperFactory>(
                new RawResourceFactory(new FhirJsonSerializer()),
                new FhirRequestContextAccessor(),
                _searchIndexer,
                _searchParameterDefinitionManager,
                Deserializers.ResourceDeserializer);

            _searchParameterStatusManager = _fixture.SearchParameterStatusManager;

            _searchService = _fixture.SearchService.CreateMockScope();
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        [Fact]
        public async Task GivenScopesWithReadForPatient_WhenRevIncludeObservations_OnlyPatientResourcesReturned()
        {
            // PUT a patient
            // PUT a related observation

            // try to query both the patient and the observation using revinclude

            // assert that only the patient is returned
        }

        private async Task<UpsertOutcome> PutResource(string testResourceFileName)
        {
            var rawResourceString = Samples.GetJson(testResourceFileName);
            ResourceElement resourceElement = Samples.GetJsonSample(testResourceFileName);

            var rawResource = new RawResource(rawResourceString, FhirResourceFormat.Json, isMetaSet: false);
            var resourceRequest = new ResourceRequest(WebRequestMethods.Http.Put);
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var searchIndices = _searchIndexer.Extract(resourceElement);
            var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, searchIndices, compartmentIndices, new List<KeyValuePair<string, string>>(), _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("Patient"));
            wrapper.SearchParameterHash = "hash";

            return await _scopedDataStore.Value.UpsertAsync(wrapper, null, true, true, CancellationToken.None);
        }

        private static async Task<FhirTypedElementToSearchValueConverterManager> CreateFhirTypedElementToSearchValueConverterManagerAsync()
        {
            var types = typeof(ITypedElementToSearchValueConverter)
                .Assembly
                .GetTypes()
                .Where(x => typeof(ITypedElementToSearchValueConverter).IsAssignableFrom(x) && !x.IsAbstract && !x.IsInterface);

            var referenceSearchValueParser = new ReferenceSearchValueParser(new FhirRequestContextAccessor());
            var codeSystemResolver = new CodeSystemResolver(ModelInfoProvider.Instance);
            await codeSystemResolver.StartAsync(CancellationToken.None);

            var fhirElementToSearchValueConverters = new List<ITypedElementToSearchValueConverter>();

            foreach (Type type in types)
            {
                // Filter out the extension converter because it will be added to the converter dictionary in the converter manager's constructor
                if (type.Name != nameof(FhirTypedElementToSearchValueConverterManager.ExtensionConverter))
                {
                    var x = (ITypedElementToSearchValueConverter)Mock.TypeWithArguments(type, referenceSearchValueParser, codeSystemResolver);
                    fhirElementToSearchValueConverters.Add(x);
                }
            }

            return new FhirTypedElementToSearchValueConverterManager(fhirElementToSearchValueConverters);
        }
    }
}
