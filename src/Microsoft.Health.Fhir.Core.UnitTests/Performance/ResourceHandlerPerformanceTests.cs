// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Http;
using BenchmarkDotNet.Attributes;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.InMemory;
using Microsoft.Health.Fhir.Core.Features.Resources.Create;
using Microsoft.Health.Fhir.Core.Features.Resources.Delete;
using Microsoft.Health.Fhir.Core.Features.Resources.Get;
using Microsoft.Health.Fhir.Core.Features.Resources.Upsert;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Mocks;
using NSubstitute;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Performance
{
    [InProcess]
    public class ResourceHandlerPerformanceTests
    {
        private IDataStore _dataStore;
        private IConformanceProvider _conformanceProvider;
        private IRawResourceFactory _rawResourceFactory;
        private IResourceWrapperFactory _resourceWrapperFactory;
        private CapabilityStatement _conformanceStatement;
        private IMediator _mediator;
        private Observation _resource;

        [GlobalSetup]
        public void Initialize()
        {
            _dataStore = new InMemoryDataStore();
            _conformanceProvider = Substitute.For<ConformanceProviderBase>();

            _rawResourceFactory = Substitute.For<RawResourceFactory>(new FhirJsonSerializer());
            _resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
            _resourceWrapperFactory
                .Create(Arg.Any<Resource>(), Arg.Any<bool>())
                .Returns(x => CreateResourceWrapper(x.ArgAt<Resource>(0), x.ArgAt<bool>(1)));

            _conformanceStatement = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(_conformanceStatement, ResourceType.Observation, null);
            var observationResource = _conformanceStatement.Rest.First().Resource.Find(x => x.Type == ResourceType.Observation);
            observationResource.ReadHistory = false;
            observationResource.UpdateCreate = true;
            observationResource.Versioning = CapabilityStatement.ResourceVersionPolicy.Versioned;

            CapabilityStatementMock.SetupMockResource(_conformanceStatement, ResourceType.Patient, null);
            var patientResource = _conformanceStatement.Rest.First().Resource.Find(x => x.Type == ResourceType.Patient);
            patientResource.ReadHistory = true;
            patientResource.UpdateCreate = true;
            patientResource.Versioning = CapabilityStatement.ResourceVersionPolicy.VersionedUpdate;

            _conformanceProvider.GetCapabilityStatementAsync().Returns(_conformanceStatement);
            var lazyConformanceProvider = new Lazy<IConformanceProvider>(() => _conformanceProvider);

            var collection = new ServiceCollection();

            collection.Add(x => new UpsertResourceHandler(_dataStore, lazyConformanceProvider, _resourceWrapperFactory)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));

            _resource = Samples.GetDefaultObservation();
            _resource.Id = null;
        }

        [Benchmark]
        public async Task UpsertPipeline()
        {
            await _mediator.UpsertResourceAsync(_resource);
        }

        private ResourceWrapper CreateResourceWrapper(Resource resource, bool isDeleted)
        {
            return new ResourceWrapper(
                resource,
                _rawResourceFactory.Create(resource),
                new ResourceRequest("http://fhir", HttpMethod.Post),
                isDeleted,
                null,
                null,
                null);
        }
    }
}
