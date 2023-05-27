// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Operations.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class SqlServerFhirDataBulkOperationTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private SqlServerFhirStorageTestsFixture _fixture;
        private SqlImportReindexer _reindexer;

        public SqlServerFhirDataBulkOperationTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            var operationsConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            operationsConfiguration.Value.Returns(new OperationsConfiguration());
            operationsConfiguration.Value.Import.DisableOptionalIndexesForImport = true;
            _reindexer = new SqlImportReindexer((SqlServerFhirDataStore)_fixture.IFhirDataStore, _fixture.SqlConnectionWrapperFactory, operationsConfiguration, NullLogger<SqlImportReindexer>.Instance);
        }

        [Fact]
        public async Task EnsureEnableDisableIndexesWorks()
        {
            await _reindexer.PreprocessAsync(CancellationToken.None);
            await CheckIndexStatuses(true);
            await _reindexer.PostprocessAsync(CancellationToken.None);
            await CheckIndexStatuses(false);
        }

        private async Task CheckIndexStatuses(bool isDisabled)
        {
            using var connection = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None);
            using var command = connection.CreateRetrySqlCommand();
            command.CommandText = @"
SELECT count(*)
  FROM sys.indexes I JOIN sys.objects O ON O.object_id = I.object_id
  WHERE O.type = 'u'
    AND I.index_id NOT IN (0,1)
    AND EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id AND name = 'PartitionScheme_ResourceTypeId')
    AND EXISTS (SELECT * FROM sys.index_columns IC JOIN sys.columns C ON C.object_id = I.object_id AND C.column_id = IC.column_id AND IC.is_included_column = 0 AND C.name = 'ResourceTypeId')
    AND O.name NOT IN ('Resource','TokenTokenCompositeSearchParam','TokenStringCompositeSearchParam')
    AND is_disabled = @IsDisabled";
            command.Parameters.AddWithValue("@IsDisabled", isDisabled);
            var cnt = await command.ExecuteScalarAsync(CancellationToken.None);
            Assert.Equal(27, cnt);
        }
    }
}
