// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.SqlServer.Features.Partitions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Partitions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class FhirRequestContextExtensionsTests
    {
        [Theory]
        [InlineData("database-a")]
        [InlineData("database.a]")]
        public void GivenSqlPartition_WhenGettingDatabaseName_ThenPartitionDatabaseIsReturned(string databaseName)
        {
            // Arrange
            var context = Substitute.For<IFhirRequestContext>();
            context.Partition.Returns(new PartitionInfo { Id = "partition-a", DatabaseName = databaseName });

            // Act
            string result = context.GetDatabaseName();

            // Assert
            Assert.Equal(databaseName, result);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GivenNoPartition_WhenGettingDatabaseTarget_ThenInvalidOperationIsThrown(bool getFormatter)
        {
            // Arrange
            var context = Substitute.For<IFhirRequestContext>();
            context.Partition.Returns((IPartitionInfo)null);

            // Act
            var exception = Record.Exception(() => GetDatabaseTarget(context, getFormatter));

            // Assert
            Assert.IsType<InvalidOperationException>(exception);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GivenNonSqlPartition_WhenGettingDatabaseTarget_ThenInvalidOperationIsThrown(bool getFormatter)
        {
            // Arrange
            var context = Substitute.For<IFhirRequestContext>();
            var partition = Substitute.For<IPartitionInfo>();
            partition.Id.Returns("partition-a");
            context.Partition.Returns(partition);

            // Act
            var exception = Record.Exception(() => GetDatabaseTarget(context, getFormatter));

            // Assert
            Assert.IsType<InvalidOperationException>(exception);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData(null, true)]
        [InlineData("", true)]
        public void GivenSqlPartitionWithoutDatabase_WhenGettingDatabaseTarget_ThenInvalidOperationIsThrown(string databaseName, bool getFormatter)
        {
            // Arrange
            var context = Substitute.For<IFhirRequestContext>();
            context.Partition.Returns(new PartitionInfo { Id = "partition-a", DatabaseName = databaseName });

            // Act
            var exception = Record.Exception(() => GetDatabaseTarget(context, getFormatter));

            // Assert
            Assert.IsType<InvalidOperationException>(exception);
        }

        [Theory]
        [InlineData("database-a", "[database-a].[dbo].[Resource]", "[database-a].[dbo].[GetResources]")]
        [InlineData("database.a]", "[database.a]]].[dbo].[Resource]", "[database.a]]].[dbo].[GetResources]")]
        public void GivenSqlPartition_WhenGettingDatabaseNameFormatter_ThenSqlObjectsTargetPartitionDatabase(
            string databaseName,
            string expectedTableName,
            string expectedProcedureName)
        {
            // Arrange
            var context = Substitute.For<IFhirRequestContext>();
            context.Partition.Returns(new PartitionInfo { Id = "partition-a", DatabaseName = databaseName });

            // Act
            var formatter = context.GetDatabaseNameFormatter();

            // Assert
            Assert.Equal(expectedTableName, formatter.GetTableName(VLatest.Resource));
            Assert.Equal(expectedProcedureName, formatter.GetStoredProcedureName("dbo.GetResources"));
        }

        [Fact]
        public void GivenChangedRequestPartition_WhenGettingDatabaseNameFormatter_ThenDatabaseSelectionIsNotCached()
        {
            // Arrange
            var context = Substitute.For<IFhirRequestContext>();
            context.Partition.Returns(new PartitionInfo { Id = "partition-a", DatabaseName = "database-a" });
            var firstFormatter = context.GetDatabaseNameFormatter();
            context.Partition.Returns(new PartitionInfo { Id = "partition-b", DatabaseName = "database-b" });

            // Act
            string databaseName = context.GetDatabaseName();
            var secondFormatter = context.GetDatabaseNameFormatter();

            // Assert
            Assert.Equal("database-b", databaseName);
            Assert.Equal("[database-a].[dbo].[Resource]", firstFormatter.GetTableName(VLatest.Resource));
            Assert.Equal("[database-b].[dbo].[Resource]", secondFormatter.GetTableName(VLatest.Resource));
        }

        private static void GetDatabaseTarget(IFhirRequestContext context, bool getFormatter)
        {
            if (getFormatter)
            {
                context.GetDatabaseNameFormatter();
            }
            else
            {
                context.GetDatabaseName();
            }
        }
    }
}
