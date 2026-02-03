// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors.QueryGenerators
{
    /// <summary>
    /// Unit tests for NotReferencedQueryGenerator.
    /// Tests the generator's basic properties and singleton pattern implementation.
    /// The actual SQL generation for not-referenced operations is handled by SearchParameterQueryGenerator.VisitNotReferenced().
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class NotReferencedQueryGeneratorTests
    {
        [Fact]
        public void GivenNotReferencedQueryGenerator_WhenInstanceAccessed_ThenNotNull()
        {
            Assert.NotNull(NotReferencedQueryGenerator.Instance);
        }

        [Fact]
        public void GivenNotReferencedQueryGenerator_WhenInstanceAccessedMultipleTimes_ThenReturnsSameInstance()
        {
            var instance1 = NotReferencedQueryGenerator.Instance;
            var instance2 = NotReferencedQueryGenerator.Instance;

            Assert.Same(instance1, instance2);
        }

        [Fact]
        public void GivenNotReferencedQueryGenerator_WhenTableAccessed_ThenReturnsResourceTable()
        {
            var table = NotReferencedQueryGenerator.Instance.Table;

            Assert.NotNull(table);
            Assert.Equal(VLatest.Resource.TableName, table.TableName);
        }

        [Fact]
        public void GivenNotReferencedQueryGenerator_WhenTableAccessedMultipleTimes_ThenReturnsConsistentTable()
        {
            var table1 = NotReferencedQueryGenerator.Instance.Table;
            var table2 = NotReferencedQueryGenerator.Instance.Table;

            Assert.Same(table1, table2);
            Assert.Equal(VLatest.Resource.TableName, table1.TableName);
            Assert.Equal(VLatest.Resource.TableName, table2.TableName);
        }

        [Fact]
        public void GivenNotReferencedQueryGenerator_WhenTypeChecked_ThenIsSearchParamTableExpressionQueryGenerator()
        {
            var instance = NotReferencedQueryGenerator.Instance;

            Assert.IsAssignableFrom<SearchParamTableExpressionQueryGenerator>(instance);
        }

        [Fact]
        public void GivenNotReferencedQueryGenerator_WhenInstanceCreated_ThenNotSameAsOtherQueryGenerators()
        {
            var notReferencedGenerator = NotReferencedQueryGenerator.Instance;
            var includeGenerator = IncludeQueryGenerator.Instance;
            var tokenGenerator = TokenQueryGenerator.Instance;
            var dateTimeGenerator = DateTimeQueryGenerator.Instance;
            var chainLinkGenerator = ChainLinkQueryGenerator.Instance;

            Assert.NotSame(notReferencedGenerator, includeGenerator);
            Assert.NotSame(notReferencedGenerator, tokenGenerator);
            Assert.NotSame(notReferencedGenerator, dateTimeGenerator);
            Assert.NotSame(notReferencedGenerator, chainLinkGenerator);
        }

        [Fact]
        public void GivenNotReferencedQueryGenerator_WhenTablePropertyAccessed_ThenOverridesBaseImplementation()
        {
            var instance = NotReferencedQueryGenerator.Instance;

            var tableProperty = instance.GetType().GetProperty("Table");
            Assert.NotNull(tableProperty);
            Assert.True(tableProperty.GetMethod.IsVirtual || tableProperty.GetMethod.IsAbstract);
        }

        [Fact]
        public void GivenNotReferencedQueryGenerator_WhenTableAccessed_ThenTableIsNotNull()
        {
            var table = NotReferencedQueryGenerator.Instance.Table;

            // This is the key difference from IncludeQueryGenerator and InQueryGenerator
            // NotReferencedQueryGenerator returns the Resource table, not null
            Assert.NotNull(table);
        }

        [Fact]
        public void GivenNotReferencedQueryGenerator_WhenComparedToGeneratorsThatReturnNullTable_ThenHasDifferentTableBehavior()
        {
            var notReferencedTable = NotReferencedQueryGenerator.Instance.Table;
            var includeTable = IncludeQueryGenerator.Instance.Table;
            var chainLinkTable = ChainLinkQueryGenerator.Instance.Table;

            // NotReferencedQueryGenerator returns a table
            Assert.NotNull(notReferencedTable);

            // These return null
            Assert.Null(includeTable);
            Assert.Null(chainLinkTable);
        }

        [Fact]
        public void GivenNotReferencedQueryGenerator_WhenTableAccessed_ThenReturnsResourceTableWithExpectedProperties()
        {
            var table = NotReferencedQueryGenerator.Instance.Table;

            Assert.NotNull(table);
            Assert.NotNull(table.TableName);
            Assert.False(string.IsNullOrEmpty(table.TableName));
            Assert.Contains("Resource", table.TableName);
        }
    }
}
