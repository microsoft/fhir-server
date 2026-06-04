// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using NSubstitute;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Smart
{
    public sealed class SmartSearchSharedContext
    {
        public SmartSearchSharedContext(
            FhirStorageTestsFixture fixture,
            IFhirOperationDataStore fhirOperationDataStore,
            IFhirStorageTestHelper fhirStorageTestHelper,
            IScoped<IFhirDataStore> scopedDataStore,
            SearchParameterDefinitionManager searchParameterDefinitionManager,
            ISupportedSearchParameterDefinitionManager supportedSearchParameterDefinitionManager,
            ITypedElementToSearchValueConverterManager typedElementToSearchValueConverterManager,
            ISearchIndexer searchIndexer,
            SearchParameterStatusManager searchParameterStatusManager)
        {
            Fixture = fixture;
            FhirOperationDataStore = fhirOperationDataStore;
            FhirStorageTestHelper = fhirStorageTestHelper;
            ScopedDataStore = scopedDataStore;
            SearchParameterDefinitionManager = searchParameterDefinitionManager;
            SupportedSearchParameterDefinitionManager = supportedSearchParameterDefinitionManager;
            TypedElementToSearchValueConverterManager = typedElementToSearchValueConverterManager;
            SearchIndexer = searchIndexer;
            SearchParameterStatusManager = searchParameterStatusManager;
        }

        public FhirStorageTestsFixture Fixture { get; }

        public IFhirOperationDataStore FhirOperationDataStore { get; }

        public IFhirStorageTestHelper FhirStorageTestHelper { get; }

        public IScoped<IFhirDataStore> ScopedDataStore { get; }

        public SearchParameterDefinitionManager SearchParameterDefinitionManager { get; }

        public ISupportedSearchParameterDefinitionManager SupportedSearchParameterDefinitionManager { get; }

        public ITypedElementToSearchValueConverterManager TypedElementToSearchValueConverterManager { get; }

        public ISearchIndexer SearchIndexer { get; }

        public SearchParameterStatusManager SearchParameterStatusManager { get; }

        public async Task<UpsertOutcome> UpsertResource(Resource resource, string httpMethod = "PUT")
        {
            resource.Meta ??= new Meta();
            resource.Meta.LastUpdated = DateTimeOffset.UtcNow;

            ResourceElement resourceElement = resource.ToResourceElement();

            var rawResource = new RawResource(resource.ToJson(), FhirResourceFormat.Json, isMetaSet: false);
            var resourceRequest = new ResourceRequest(httpMethod);
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var searchIndices = SearchIndexer.Extract(resourceElement);
            var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, searchIndices, compartmentIndices, new List<KeyValuePair<string, string>>(), SearchParameterDefinitionManager.GetSearchParameterHashForResourceType("Patient"));
            wrapper.SearchParameterHash = "hash";

            return await ScopedDataStore.Value.UpsertAsync(new ResourceWrapperOperation(wrapper, true, true, null, false, false, bundleResourceContext: null), CancellationToken.None);
        }
    }
}
