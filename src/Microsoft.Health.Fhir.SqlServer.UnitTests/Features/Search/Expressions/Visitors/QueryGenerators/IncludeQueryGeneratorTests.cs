// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors.QueryGenerators
{
    /// <summary>
    /// Unit tests for IncludeQueryGenerator.
    /// Tests the generator's basic properties and singleton pattern implementation.
    /// The actual SQL generation for include operations is handled by SqlQueryGenerator.HandleTableKindInclude().
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class IncludeQueryGeneratorTests
    {
        [Fact]
        public void GivenIncludeQueryGenerator_WhenInstanceAccessed_ThenNotNull()
        {
            Assert.NotNull(IncludeQueryGenerator.Instance);
        }

        [Fact]
        public void GivenIncludeQueryGenerator_WhenInstanceAccessedMultipleTimes_ThenReturnsSameInstance()
        {
            var instance1 = IncludeQueryGenerator.Instance;
            var instance2 = IncludeQueryGenerator.Instance;

            Assert.Same(instance1, instance2);
        }

        [Fact]
        public void GivenIncludeQueryGenerator_WhenTableAccessed_ThenReturnsNull()
        {
            var table = IncludeQueryGenerator.Instance.Table;

            Assert.Null(table);
        }

        [Fact]
        public void GivenIncludeQueryGenerator_WhenTypeChecked_ThenIsSearchParamTableExpressionQueryGenerator()
        {
            var instance = IncludeQueryGenerator.Instance;

            Assert.IsAssignableFrom<SearchParamTableExpressionQueryGenerator>(instance);
        }

        [Fact]
        public void GivenIncludeQueryGenerator_WhenInstanceCreated_ThenNotSameAsOtherQueryGenerators()
        {
            var includeGenerator = IncludeQueryGenerator.Instance;
            var tokenGenerator = TokenQueryGenerator.Instance;
            var dateTimeGenerator = DateTimeQueryGenerator.Instance;
            var chainLinkGenerator = ChainLinkQueryGenerator.Instance;

            Assert.NotSame(includeGenerator, tokenGenerator);
            Assert.NotSame(includeGenerator, dateTimeGenerator);
            Assert.NotSame(includeGenerator, chainLinkGenerator);
        }

        [Fact]
        public void GivenIncludeQueryGenerator_WhenTablePropertyAccessed_ThenOverridesBaseImplementation()
        {
            var instance = IncludeQueryGenerator.Instance;

            var tableProperty = instance.GetType().GetProperty("Table");
            Assert.NotNull(tableProperty);
            Assert.True(tableProperty.GetMethod.IsVirtual || tableProperty.GetMethod.IsAbstract);
        }
    }
}
