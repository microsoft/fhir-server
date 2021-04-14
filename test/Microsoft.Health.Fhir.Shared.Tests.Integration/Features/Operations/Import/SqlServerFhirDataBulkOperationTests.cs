// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Operations.Import
{
    public class SqlServerFhirDataBulkOperationTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private SqlServerFhirStorageTestsFixture _fixture;

        public SqlServerFhirDataBulkOperationTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
        }

        /*
        [Fact]
        public Task GivenListOfTasksInQueue_WhenGetNextTask_AvailableTasksShouldBeReturned()
        {
            SqlServerFhirDataBulkOperation sqlServerFhirDataBulkOperation = new SqlServerFhirDataBulkOperation(_fixture.SqlConnectionWrapperFactory, new TestSqlServerTransientFaultRetryPolicyFactory(), NullLogger<SqlServerFhirDataBulkOperation>.Instance);
            // sqlServerFhirDataBulkOperation

        }

        public DataTable GetSampleResourceTable(long startSuggoratedId, int count)
        {
            ResourceTableBulkCopyDataGenerator d;

            DataTable table = new DataTable("dbo.Resource");
            table.Columns.Add(new DataColumn(VLatest.Resource.ResourceTypeId.Metadata.Name, VLatest.Resource.ResourceTypeId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.ResourceId.Metadata.Name, VLatest.Resource.ResourceId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.Version.Metadata.Name, VLatest.Resource.Version.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.IsHistory.Metadata.Name, VLatest.Resource.IsHistory.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.ResourceSurrogateId.Metadata.Name, VLatest.Resource.ResourceSurrogateId.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.IsDeleted.Metadata.Name, VLatest.Resource.IsDeleted.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.RequestMethod.Metadata.Name, VLatest.Resource.RequestMethod.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.RawResource.Metadata.Name, VLatest.Resource.RawResource.Metadata.SqlDbType.GetGeneralType()));
            table.Columns.Add(new DataColumn(VLatest.Resource.IsRawResourceMetaSet.Metadata.Name, VLatest.Resource.IsRawResourceMetaSet.Metadata.SqlDbType.GetGeneralType()));
        } */
    }
}
