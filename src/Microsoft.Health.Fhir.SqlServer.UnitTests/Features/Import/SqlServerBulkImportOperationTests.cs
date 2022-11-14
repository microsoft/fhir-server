// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class SqlServerBulkImportOperationTests
    {
        private SqlImportOperation _sqlServerFhirDataBulkOperation;

        public SqlServerBulkImportOperationTests()
        {
            var operationsConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            operationsConfiguration.Value.Returns(new OperationsConfiguration());

            var schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(Substitute.For<SqlTransactionHandler>(), Substitute.For<ISqlConnectionBuilder>(), SqlConfigurableRetryFactory.CreateNoneRetryProvider(), Options.Create(new SqlServerDataStoreConfiguration()));

            _sqlServerFhirDataBulkOperation = new SqlImportOperation(sqlConnectionWrapperFactory, Substitute.For<ISqlServerFhirModel>(), operationsConfiguration, schemaInformation, NullLogger<SqlImportOperation>.Instance);
        }

        [Fact]
        public void GivenResourceRelatedTables_WhenNewIndexesAdded_BulkImportOperationShouldSupportNewIndexes()
        {
            Table[] resourceRelatedTables = new Table[]
            {
                VLatest.Resource,
                VLatest.ResourceWriteClaim,
                VLatest.CompartmentAssignment,
                VLatest.DateTimeSearchParam,
                VLatest.NumberSearchParam,
                VLatest.QuantitySearchParam,
                VLatest.ReferenceSearchParam,
                VLatest.ReferenceTokenCompositeSearchParam,
                VLatest.StringSearchParam,
                VLatest.TokenDateTimeCompositeSearchParam,
                VLatest.TokenNumberNumberCompositeSearchParam,
                VLatest.TokenQuantityCompositeSearchParam,
                VLatest.TokenSearchParam,
                VLatest.TokenStringCompositeSearchParam,
                VLatest.TokenText,
                VLatest.TokenTokenCompositeSearchParam,
                VLatest.UriSearchParam,
            };

            string[] excludeIndexNames = new string[]
            {
                "IX_Resource_ResourceTypeId_ResourceId_Version",
                "IX_Resource_ResourceTypeId_ResourceId",
                "IX_Resource_ResourceTypeId_ResourceSurrgateId",
                "IX_Resource_ResourceSurrogateId",
            };

            string[] supportedIndexesNames = _sqlServerFhirDataBulkOperation.IndexesList().Select(i => i.index.IndexName).ToArray();
            int expectedIndexesCount = 0;
            foreach (Table table in resourceRelatedTables)
            {
                string[] indexNames = table.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Where(f => f.Name.StartsWith("IX_")).Select(f => f.Name).ToArray();
                foreach (string indexName in indexNames)
                {
                    if (excludeIndexNames.Contains(indexName))
                    {
                        continue;
                    }

                    Assert.Contains(indexName, supportedIndexesNames);
                    expectedIndexesCount++;
                }
            }

            Assert.Equal(expectedIndexesCount, supportedIndexesNames.Length);
        }
    }
}
