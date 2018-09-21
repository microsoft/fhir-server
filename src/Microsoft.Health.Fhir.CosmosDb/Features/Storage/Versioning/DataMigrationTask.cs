// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning.DataMigrations;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning
{
    public class DataMigrationTask : BackgroundService
    {
        private readonly CosmosDataStoreConfiguration _configuration;
        private readonly Func<IScoped<IDocumentClient>> _documentClientProvider;
        private readonly ICosmosDbDistributedLockFactory _distributedLockFactory;
        private readonly ICosmosDocumentQueryFactory _queryFactory;
        private readonly ILogger<DataMigrationTask> _logger;
        private readonly IEnumerable<Migration> _migrations;
        private readonly Random _random = new Random();
        private const int DaysToContinueMonitoring = 7;
        private const string PartitionRangeKeyField = "partitionRangeKey";

        public DataMigrationTask(
            CosmosDataStoreConfiguration configuration,
            Func<IScoped<IDocumentClient>> documentClientProvider,
            ICosmosDbDistributedLockFactory distributedLockFactory,
            ICosmosDocumentQueryFactory queryFactory,
            ILogger<DataMigrationTask> logger,
            IEnumerable<Migration> migrations)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(documentClientProvider, nameof(documentClientProvider));
            EnsureArg.IsNotNull(distributedLockFactory, nameof(distributedLockFactory));
            EnsureArg.IsNotNull(queryFactory, nameof(queryFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(migrations, nameof(migrations));

            _configuration = configuration;
            _documentClientProvider = documentClientProvider;
            _distributedLockFactory = distributedLockFactory;
            _queryFactory = queryFactory;
            _logger = logger;
            _migrations = migrations;
        }

        /// <summary>
        /// Manages a distributed lock on a partitionRangeKey for which to execute migrations
        /// </summary>
        /// <param name="cancellationToken">A cancellation token</param>
        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var partitionKeyRanges = new List<string>();

                using (IScoped<IDocumentClient> documentClient = _documentClientProvider.Invoke())
                {
                    Uri partitionKeyRangesUri = UriFactory.CreatePartitionKeyRangesUri(_configuration.DatabaseId, _configuration.CollectionId);
                    ICollection<string> response = await ReadPartitionKeyRangeFeedAsync(documentClient, partitionKeyRangesUri);
                    partitionKeyRanges.AddRange(response);
                }

                foreach (var partitionKeyRange in partitionKeyRanges)
                {
                    using (ICosmosDbDistributedLock distributedLock = _distributedLockFactory.Create(_configuration.RelativeCollectionUri, $"Lock:DataMigration:{partitionKeyRange}"))
                    {
                        if (await distributedLock.TryAcquireLock())
                        {
                            await PerformDataMigrations(partitionKeyRange, cancellationToken);
                            await distributedLock.ReleaseLock();
                        }
                    }
                }

                await Task.Delay(GetWaitInterval(), cancellationToken);
            }
        }

        /// <summary>
        /// Executes all pending data migrations against the specified partitionRangeKey
        /// </summary>
        /// <param name="partitionRangeKey">The partition range key</param>
        /// <param name="cancellationToken">The cancellation token</param>
        protected internal virtual async Task PerformDataMigrations(string partitionRangeKey, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(partitionRangeKey, nameof(partitionRangeKey));

            var executedMigrations = new Dictionary<string, DataMigration>();

            using (IScoped<IDocumentClient> documentClient = _documentClientProvider.Invoke())
            {
                IDocumentQuery<DataMigration> query = _queryFactory.Create<DataMigration>(
                    documentClient.Value,
                    new CosmosQueryContext(
                        _configuration.RelativeCollectionUri,
                        new SqlQuerySpec($"SELECT * FROM c WHERE c.{PartitionRangeKeyField} = @{PartitionRangeKeyField}", new SqlParameterCollection(new[] { new SqlParameter($"@{PartitionRangeKeyField}", partitionRangeKey) })),
                        new FeedOptions { PartitionKey = new PartitionKey(DataMigration.DataMigrationPartition) }));

                do
                {
                    FeedResponse<DataMigration> results = await query.ExecuteNextAsync<DataMigration>(cancellationToken);
                    foreach (DataMigration item in results)
                    {
                        executedMigrations.Add($"{item.Name}.{item.PartitionRangeKey}", item);
                    }
                }
                while (query.HasMoreResults);
            }

            var migrations = _migrations
                .Select(x => (Migration: x, Record: executedMigrations.TryGetValue($"{x.Name}.{partitionRangeKey}", out DataMigration executed) ? executed : new DataMigration(x.Name, partitionRangeKey)));

            // This executes pending migrations, and migrations completed recently.
            // The idea is to continue to monitor for entities to upgrade while there may be older instances of the code still active (support for rolling migrations).
            var pendingMigrations = migrations.Where(x => x.Record.Completed.GetValueOrDefault(Clock.UtcNow) > Clock.UtcNow.AddDays(-DaysToContinueMonitoring)).ToArray();

            for (var i = 0; i < pendingMigrations.Length; i++)
            {
                // For each migration, the current and newer migrations are used to migrate a selected document to the latest version.
                var pending = pendingMigrations.Skip(i).ToArray();
                await ExecuteMigration(pending, cancellationToken);
            }
        }

        protected internal virtual async Task ExecuteMigration((Migration Migration, DataMigration Record)[] pendingMigrations, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var migrationPair = pendingMigrations.First();

            using (IScoped<IDocumentClient> documentClient = _documentClientProvider.Invoke())
            {
                await documentClient.Value.UpsertDocumentAsync(_configuration.RelativeCollectionUri, migrationPair.Record);
            }

            bool success = true;

            try
            {
                ICollection<Document> documentsToMigrate;

                do
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Since every result item is to be modified, the query is re-created on each iteration
                    documentsToMigrate = await CreateMigrationQuery(migrationPair, cancellationToken);

                    foreach (Document item in documentsToMigrate)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        await UpdateDocument(item, pendingMigrations.Select(x => x.Migration), cancellationToken);
                    }
                }
                while (documentsToMigrate.Any());
            }
            catch (Exception ex)
            {
                migrationPair.Record.LastException = ex.ToString();
                success = false;
                _logger.LogError(ex, "Unable to complate data migration step {DataMigrationName}", migrationPair.Migration.Name);
            }

            if (success && !migrationPair.Record.Completed.HasValue)
            {
                migrationPair.Record.Completed = Clock.UtcNow;
            }

            using (IScoped<IDocumentClient> documentClient = _documentClientProvider.Invoke())
            {
                await documentClient.Value.UpsertDocumentAsync(_configuration.RelativeCollectionUri, migrationPair.Record);
            }
        }

        private async Task<ICollection<Document>> CreateMigrationQuery((Migration Migration, DataMigration Record) migrationPair, CancellationToken cancellationToken)
        {
            using (IScoped<IDocumentClient> documentClient = _documentClientProvider.Invoke())
            {
                IDocumentQuery<Document> query = _queryFactory.Create<Document>(
                    documentClient.Value,
                    new CosmosQueryContext(
                        _configuration.RelativeCollectionUri,
                        migrationPair.Migration.DocumentsToMigrate(),
                        new FeedOptions
                        {
                            PartitionKeyRangeId = migrationPair.Record.PartitionRangeKey,
                            EnableCrossPartitionQuery = true,
                            MaxItemCount = _configuration.DataMigrationBatchSize,
                        }));

                FeedResponse<Document> results = await query.ExecuteNextAsync<Document>(cancellationToken);

                return results.ToArray();
            }
        }

        protected internal virtual async Task<bool> UpdateDocument(Document item, IEnumerable<Migration> migrations, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(item, nameof(item));
            EnsureArg.IsNotNull(migrations, nameof(migrations));

            using (IScoped<IDocumentClient> documentClient = _documentClientProvider.Invoke())
            {
                try
                {
                    foreach (IUpdateOperation action in migrations.SelectMany(x => x.Migrate(item)))
                    {
                        action.Apply(item);
                    }

                    var accessCondition = new AccessCondition { Condition = item.ETag, Type = AccessConditionType.IfMatch };
                    await documentClient.Value.ReplaceDocumentAsync(item.SelfLink, item, new RequestOptions { AccessCondition = accessCondition });

                    return true;
                }
                catch (DocumentClientException dce) when (dce.StatusCode == (HttpStatusCode)429)
                {
                    // Doubles retry delay
                    await Task.Delay(dce.RetryAfter.Add(dce.RetryAfter), cancellationToken);
                }
                catch (DocumentClientException dce)
                {
                    _logger.LogError(dce, "Unable to complete migration on '{DocumentId}", item.Id);
                }
            }

            return false;
        }

        protected internal virtual async Task<ICollection<string>> ReadPartitionKeyRangeFeedAsync(IScoped<IDocumentClient> documentClient, Uri partitionKeyRangesUri)
        {
            EnsureArg.IsNotNull(documentClient, nameof(documentClient));
            EnsureArg.IsNotNull(partitionKeyRangesUri, nameof(partitionKeyRangesUri));

            Task<FeedResponse<PartitionKeyRange>> query = ((DocumentClient)documentClient.Value).ReadPartitionKeyRangeFeedAsync(partitionKeyRangesUri, new FeedOptions { MaxItemCount = _configuration.DataMigrationBatchSize });
            FeedResponse<PartitionKeyRange> response;

            var partitionKeyRanges = new List<string>();
            do
            {
                response = await query;

                foreach (PartitionKeyRange item in response)
                {
                    partitionKeyRanges.Add(item.Id);
                }
            }
            while (!string.IsNullOrEmpty(response.ResponseContinuation));

            return partitionKeyRanges;
        }

        protected internal virtual TimeSpan GetWaitInterval()
        {
            // Wait between 1 and 5 minutes between attempts
            return TimeSpan.FromSeconds(_random.Next(60, 60 * 5));
        }
    }
}
