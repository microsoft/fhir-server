// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Schema
{
    public class SchemaJobWorkerTests
    {
        private readonly ISchemaDataStore _schemaDataStore = Substitute.For<ISchemaDataStore>();
        private readonly SqlServerDataStoreConfiguration _sqlServerDataStoreConfiguration = new SqlServerDataStoreConfiguration();
        private readonly SchemaInformation schemaInformation = new SchemaInformation();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;

        private readonly SchemaJobWorker _schemaJobWorker;

        public SchemaJobWorkerTests()
        {
            var scopedSchemaDataStore = Substitute.For<IScoped<ISchemaDataStore>>();
            scopedSchemaDataStore.Value.Returns(_schemaDataStore);

            _schemaJobWorker = new SchemaJobWorker(
                () => scopedSchemaDataStore,
                Options.Create(_sqlServerDataStoreConfiguration),
                NullLogger<SchemaJobWorker>.Instance);

            _cancellationToken = _cancellationTokenSource.Token;
        }

        [Fact]
        public async Task GivenSchemaBackgroundJob_WhenExecutedWithCancellationToken_ThenOnlyInsertExecuted()
        {
            _cancellationTokenSource.CancelAfter(TimeSpan.FromMilliseconds(0));

            await _schemaJobWorker.ExecuteAsync(schemaInformation, "instanceName", _cancellationToken);
            await _schemaDataStore.Received().InsertInstanceSchemaInformation("instanceName", schemaInformation, _cancellationToken);
            await _schemaDataStore.DidNotReceive().GetLatestCompatibleVersionAsync(_cancellationToken);
        }

        [Fact]
        public async Task GivenSchemaBackgroundJob_WhenExecuted_ThenInsertAndPollingIsExecuted()
        {
            _cancellationTokenSource.CancelAfter(TimeSpan.FromMilliseconds(100));

            await _schemaJobWorker.ExecuteAsync(schemaInformation, "instanceName", _cancellationToken);
            await _schemaDataStore.Received().GetLatestCompatibleVersionAsync(_cancellationToken);
            await _schemaDataStore.Received().InsertInstanceSchemaInformation("instanceName", schemaInformation, _cancellationToken);
        }
    }
}
