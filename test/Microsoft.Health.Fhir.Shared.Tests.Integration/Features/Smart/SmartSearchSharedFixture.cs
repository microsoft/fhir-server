// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Smart
{
    public sealed class SmartSearchSharedFixture : IAsyncLifetime
    {
        private readonly DataStore _dataStore;
        private FhirStorageTestsFixture _fixture;
        private IScoped<IFhirDataStore> _scopedDataStore;
        private SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private ISearchIndexer _searchIndexer;

        public SmartSearchSharedFixture(DataStore dataStore)
        {
            _dataStore = dataStore;
        }

        public FhirStorageTestsFixture Fixture => _fixture;

        public async Task InitializeAsync()
        {
            if (ModelInfoProvider.Instance.Version != FhirSpecification.R4 &&
                ModelInfoProvider.Instance.Version != FhirSpecification.R4B)
            {
                return;
            }

            _fixture = new FhirStorageTestsFixture(_dataStore);
            await _fixture.InitializeAsync();

            var typedElementToSearchValueConverterManager = await CreateFhirTypedElementToSearchValueConverterManagerAsync();
            _searchIndexer = new TypedElementSearchIndexer(
                _fixture.SupportedSearchParameterDefinitionManager,
                typedElementToSearchValueConverterManager,
                Substitute.For<IReferenceToElementResolver>(),
                ModelInfoProvider.Instance,
                NullLogger<TypedElementSearchIndexer>.Instance);
            _searchParameterDefinitionManager = _fixture.SearchParameterDefinitionManager;
            _scopedDataStore = _fixture.DataStore.CreateMockScope();

            await LoadBundleAsync("SmartPatientA");
            await LoadBundleAsync("SmartPatientB");
            await LoadBundleAsync("SmartPatientC");
            await LoadBundleAsync("SmartPatientD");
            await LoadBundleAsync("SmartCommon");

            await UpsertResource(Samples.GetJsonSample<Medication>("Medication"));
            await UpsertResource(Samples.GetJsonSample<Organization>("Organization"));
            await UpsertResource(Samples.GetJsonSample<Location>("Location-example-hq"));
        }

        public async Task DisposeAsync()
        {
            if (_fixture != null)
            {
                await _fixture.DisposeAsync();
            }
        }

        public async Task<UpsertOutcome> UpsertResource(Resource resource, string httpMethod = "PUT")
        {
            resource.Meta ??= new Meta();
            resource.Meta.LastUpdated = DateTimeOffset.UtcNow;

            ResourceElement resourceElement = resource.ToResourceElement();

            var rawResource = new RawResource(resource.ToJson(), FhirResourceFormat.Json, isMetaSet: false);
            var resourceRequest = new ResourceRequest(httpMethod);
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var searchIndices = _searchIndexer.Extract(resourceElement);
            var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, searchIndices, compartmentIndices, new List<KeyValuePair<string, string>>(), _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("Patient"));
            wrapper.SearchParameterHash = "hash";

            return await _scopedDataStore.Value.UpsertAsync(new ResourceWrapperOperation(wrapper, true, true, null, false, false, bundleResourceContext: null), CancellationToken.None);
        }

        private static async Task<FhirTypedElementToSearchValueConverterManager> CreateFhirTypedElementToSearchValueConverterManagerAsync()
        {
            var types = typeof(ITypedElementToSearchValueConverter)
                .Assembly
                .GetTypes()
                .Where(x => typeof(ITypedElementToSearchValueConverter).IsAssignableFrom(x) && !x.IsAbstract && !x.IsInterface);

            var referenceSearchValueParser = new ReferenceSearchValueParser(new FhirRequestContextAccessor(), new FhirServerInstanceConfiguration());
            var codeSystemResolver = new CodeSystemResolver(ModelInfoProvider.Instance);
            await codeSystemResolver.StartAsync(CancellationToken.None);

            var fhirElementToSearchValueConverters = new List<ITypedElementToSearchValueConverter>();

            foreach (Type type in types.Where(type => type.Name != nameof(FhirTypedElementToSearchValueConverterManager.ExtensionConverter)))
            {
                // Filter out the extension converter because it will be added to the converter dictionary in the converter manager's constructor
                var x = (ITypedElementToSearchValueConverter)Mock.TypeWithArguments(type, referenceSearchValueParser, codeSystemResolver);
                fhirElementToSearchValueConverters.Add(x);
            }

            return new FhirTypedElementToSearchValueConverterManager(fhirElementToSearchValueConverters);
        }

        private async Task LoadBundleAsync(string sampleName)
        {
            var smartBundle = Samples.GetJsonSample<Bundle>(sampleName);

            foreach (var entry in smartBundle.Entry)
            {
                await UpsertResource(entry.Resource);
            }
        }
    }
}
