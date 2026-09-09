// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Operations;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Operations.Reindex
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    public class VectorSearchSourceRefreshJobTests
    {
        private readonly IVectorSearchSourceDependencyStore _dependencyStore = Substitute.For<IVectorSearchSourceDependencyStore>();
        private readonly IFhirDataStore _fhirDataStore = Substitute.For<IFhirDataStore>();
        private readonly IResourceWrapperFactory _resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
        private readonly IVectorSearchIndexer _vectorSearchIndexer = Substitute.For<IVectorSearchIndexer>();

        public VectorSearchSourceRefreshJobTests()
        {
            ModelInfoProvider.SetProvider(
                MockModelInfoProviderBuilder.Create(FhirSpecification.R4)
                    .AddKnownTypes(KnownResourceTypes.DocumentReference)
                    .Build());
        }

        [Fact]
        public async Task GivenNoDependentResources_WhenExecuted_ThenNoIndexUpdateOccurs()
        {
            VectorSearchSourceRefreshJob job = CreateJob();
            JobInfo jobInfo = CreateJobInfo();

            await job.ExecuteAsync(jobInfo, CancellationToken.None);

            await _fhirDataStore.DidNotReceive().GetAsync(Arg.Any<IReadOnlyList<ResourceKey>>(), Arg.Any<CancellationToken>());
            await _vectorSearchIndexer.DidNotReceive().IndexAsync(Arg.Any<IReadOnlyCollection<ResourceWrapper>>(), Arg.Any<CancellationToken>());
            await _fhirDataStore.DidNotReceive().BulkUpdateSearchParameterIndicesAsync(Arg.Any<IReadOnlyCollection<ResourceWrapper>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenDependentResources_WhenExecuted_ThenCurrentOwnersAreVectorIndexedAndPersisted()
        {
            var ownerKey = new ResourceKey("DocumentReference", "owner");
            ResourceWrapper owner = CreateResourceWrapper(ownerKey);
            _dependencyStore.GetDependentResourceKeysAsync("Binary", "source", Arg.Any<CancellationToken>()).Returns(new[] { ownerKey });
            _fhirDataStore.GetAsync(Arg.Any<IReadOnlyList<ResourceKey>>(), Arg.Any<CancellationToken>()).Returns(new[] { owner });
            VectorSearchSourceRefreshJob job = CreateJob();

            await job.ExecuteAsync(CreateJobInfo(), CancellationToken.None);

            _resourceWrapperFactory.Received(1).Update(owner);
            await _vectorSearchIndexer.Received(1).IndexAsync(
                Arg.Is<IReadOnlyCollection<ResourceWrapper>>(resources => resources.Count == 1 && resources.Contains(owner)),
                CancellationToken.None);
            await _fhirDataStore.Received(1).BulkUpdateSearchParameterIndicesAsync(
                Arg.Is<IReadOnlyCollection<ResourceWrapper>>(resources => resources.Count == 1 && resources.Contains(owner)),
                CancellationToken.None);
        }

        [Fact]
        public async Task GivenDependentOwnerWasDeleted_WhenExecuted_ThenNoIndexUpdateOccurs()
        {
            var ownerKey = new ResourceKey("DocumentReference", "owner");
            _dependencyStore.GetDependentResourceKeysAsync("Binary", "source", Arg.Any<CancellationToken>()).Returns(new[] { ownerKey });
            _fhirDataStore.GetAsync(Arg.Any<IReadOnlyList<ResourceKey>>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<ResourceWrapper>());
            VectorSearchSourceRefreshJob job = CreateJob();

            await job.ExecuteAsync(CreateJobInfo(), CancellationToken.None);

            await _vectorSearchIndexer.DidNotReceive().IndexAsync(Arg.Any<IReadOnlyCollection<ResourceWrapper>>(), Arg.Any<CancellationToken>());
            await _fhirDataStore.DidNotReceive().BulkUpdateSearchParameterIndicesAsync(Arg.Any<IReadOnlyCollection<ResourceWrapper>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenOwnerVersionConflict_WhenExecuted_ThenJobSoftFailsForRetry()
        {
            var ownerKey = new ResourceKey("DocumentReference", "owner");
            ResourceWrapper owner = CreateResourceWrapper(ownerKey);
            _dependencyStore.GetDependentResourceKeysAsync("Binary", "source", Arg.Any<CancellationToken>()).Returns(new[] { ownerKey });
            _fhirDataStore.GetAsync(Arg.Any<IReadOnlyList<ResourceKey>>(), Arg.Any<CancellationToken>()).Returns(new[] { owner });
            _fhirDataStore.BulkUpdateSearchParameterIndicesAsync(Arg.Any<IReadOnlyCollection<ResourceWrapper>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new PreconditionFailedException("conflict")));
            VectorSearchSourceRefreshJob job = CreateJob();

            await Assert.ThrowsAsync<JobExecutionSoftFailureException>(() => job.ExecuteAsync(CreateJobInfo(), CancellationToken.None));
        }

        private VectorSearchSourceRefreshJob CreateJob()
        {
            IScoped<IFhirDataStore> scope = Substitute.For<IScoped<IFhirDataStore>>();
            scope.Value.Returns(_fhirDataStore);

            return new VectorSearchSourceRefreshJob(
                _dependencyStore,
                () => scope,
                _resourceWrapperFactory,
                NullLogger<VectorSearchSourceRefreshJob>.Instance,
                _vectorSearchIndexer);
        }

        private static JobInfo CreateJobInfo()
        {
            return new JobInfo
            {
                Id = 1,
                GroupId = 1,
                QueueType = (byte)QueueType.Reindex,
                Definition = JsonConvert.SerializeObject(new VectorSearchSourceRefreshJobDefinition
                {
                    TypeId = (int)JobType.VectorSearchSourceRefresh,
                    SourceResourceType = "Binary",
                    SourceResourceId = "source",
                    SourceResourceVersion = "2",
                }),
            };
        }

        private static ResourceWrapper CreateResourceWrapper(ResourceKey key)
        {
            return new ResourceWrapper(
                key.Id,
                "1",
                key.ResourceType,
                new RawResource("{}", FhirResourceFormat.Json, isMetaSet: false),
                null,
                DateTimeOffset.MinValue,
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null);
        }
    }
}
