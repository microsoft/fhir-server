// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning.DataMigrations;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage.Versioning
{
    public class DataMigrationTaskTests
    {
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration = new CosmosDataStoreConfiguration
        {
            AllowDatabaseCreation = false,
            CollectionId = "testcollectionid",
            ConnectionMode = Azure.Documents.Client.ConnectionMode.Direct,
            ConnectionProtocol = Azure.Documents.Client.Protocol.Https,
            DatabaseId = "testdatabaseid",
            Host = "https://fakehost",
            Key = "ZmFrZWtleQ==",   // "fakekey"
            PreferredLocations = null,
        };

        private static readonly string PartitionRangeKey = "1";
        private readonly DataMigrationTask _task;
        private readonly int _maxTimeout = 10;
        private readonly ICosmosDbDistributedLock _cosmosDbDistributedLock;
        private readonly CancellationTokenSource _stoppingSource;
        private readonly IDocumentClient _client;
        private readonly DataMigration[] _dataMigration = { new DataMigration(nameof(ExecutedTestMigration), PartitionRangeKey) { Completed = Clock.UtcNow.AddDays(-8) } };
        private readonly V001AddsVersionPropertyToAllDocuments _migration = new V001AddsVersionPropertyToAllDocuments();
        private readonly ICosmosDocumentQueryFactory _queryFactory;

        public DataMigrationTaskTests()
        {
            _client = Substitute.For<IDocumentClient>();
            var scopedDocumentClientProvider = Substitute.For<Func<IScoped<IDocumentClient>>>();
            scopedDocumentClientProvider.Invoke().Returns(new DocumentClientScope(_client));

            var lockFactory = Substitute.For<ICosmosDbDistributedLockFactory>();
            _cosmosDbDistributedLock = Substitute.For<ICosmosDbDistributedLock>();
            _queryFactory = Substitute.For<ICosmosDocumentQueryFactory>();

            lockFactory.Create(Arg.Any<Uri>(), Arg.Any<string>()).Returns(_cosmosDbDistributedLock);
            _cosmosDbDistributedLock.TryAcquireLock().Returns(true);

            _task = Substitute.ForPartsOf<DataMigrationTask>(
                _cosmosDataStoreConfiguration,
                scopedDocumentClientProvider,
                lockFactory,
                _queryFactory,
                NullLogger<DataMigrationTask>.Instance,
                new Migration[] { _migration, new ExecutedTestMigration() });

            _task.ReadPartitionKeyRangeFeedAsync(Arg.Any<IScoped<IDocumentClient>>(), Arg.Any<Uri>())
                .Returns(new[] { PartitionRangeKey });

            _stoppingSource = new CancellationTokenSource(TimeSpan.FromSeconds(_maxTimeout));
        }

        [Fact]
        public async Task GivenACollection_WhenMigratingData_ThenMigrationsAreRunWhenLockIsAquired()
        {
            _task.PerformDataMigrations(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask)
                .AndDoes(x => _stoppingSource.Cancel());

            await _task.StartAsync(_stoppingSource.Token);

            await _task.Received(1).PerformDataMigrations(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenACollection_WhenMigratingData_ThenMigrationsAreNotRunWhenLockIsNotAquired()
        {
            _cosmosDbDistributedLock.TryAcquireLock().Returns(false);

            _task.GetWaitInterval()
                .Returns(TimeSpan.FromSeconds(_maxTimeout))
                .AndDoes(x => _stoppingSource.Cancel());

            await _task.StartAsync(_stoppingSource.Token);

            await _task.DidNotReceive().PerformDataMigrations(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenACollection_WhenSettingUpMigratingData_ThenNewMigrationsAreExecutedAndExecutedAreExcluded()
        {
            var cosmosDocumentQuery = Substitute.For<IDocumentQuery<DataMigration>>();
            cosmosDocumentQuery.ExecuteNextAsync<DataMigration>(Arg.Any<CancellationToken>())
                .Returns(new FeedResponse<DataMigration>(_dataMigration));

            _queryFactory
                .Create<DataMigration>(_client, Arg.Any<CosmosQueryContext>())
                .Returns(cosmosDocumentQuery);

            _task.ExecuteMigration(Arg.Any<(Migration Migration, DataMigration Record)[]>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            await _task.PerformDataMigrations(PartitionRangeKey, _stoppingSource.Token);

            await _task.Received(1)
                .ExecuteMigration(
                    Arg.Any<(Migration Migration, DataMigration Record)[]>(),
                    Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenACollection_WhenExecutingMigration_ThenStatusSavedWhenMigrationStarts()
        {
            DateTimeOffset instant = DateTimeOffset.UtcNow;

            using (Mock.Property(() => Clock.UtcNowFunc, () => instant))
            {
                _task.UpdateDocument(Arg.Any<Document>(), Arg.Any<IEnumerable<Migration>>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult(true));

                (Migration _migration, DataMigration) migrationPair = (_migration, new DataMigration(_migration.Name, PartitionRangeKey));
                await _task.ExecuteMigration(new[] { migrationPair }, _stoppingSource.Token);

                // Updated when migration kicks off
                await _client
                    .Received()
                    .UpsertDocumentAsync(Arg.Any<Uri>(), Arg.Is<DataMigration>(x => x.Completed == null), Arg.Any<RequestOptions>(), Arg.Any<bool>());
            }
        }
    }
}
