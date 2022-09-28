// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    [Trait(Traits.Category, Categories.Search)]
    public class SqlCommandSimplifierTests
    {
        private ILogger _logger;

        public SqlCommandSimplifierTests()
        {
            _logger = Substitute.For<ILogger>();
        }

        [Fact]
        public void GivenACommandWithDistinct_WhenSimplified_ThenTheDistinctIsRemoved()
        {
            string startingString = "select distinct * from Resource where Resource.IsDeleted = 0 and Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            string expectedString = "select * from Resource where Resource.IsDeleted = 0 and Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            IndentedStringBuilder stringBuilder = new IndentedStringBuilder(new StringBuilder(startingString));
            SqlParameterCollection sqlParameterCollection = RuntimeHelpers.GetUninitializedObject(typeof(SqlParameterCollection)) as SqlParameterCollection;
            SqlCommandSimplifier.RemoveRedundantParameters(stringBuilder, sqlParameterCollection, _logger);
            string actualString = stringBuilder.ToString();
            Assert.Equal(expectedString, actualString);
        }

        [Fact]
        public void GivenACommandWithRedundantConditions_WhenSimplified_ThenOnlyOneConditionIsLeft()
        {
            string startingString = "select distinct * from Resource where Resource.ResourceSurrogateId >= @p1 and Resource.ResourceSurrogateId > @p2 and Resource.ResourceSurrogateId <= @p3 and Resource.ResourceSurrogateId < @p4 and Resource.IsDeleted = 0 and Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            string expectedString = "select * from Resource where Resource.ResourceSurrogateId >= @p1 and 1 = 1 and 1 = 1 and Resource.ResourceSurrogateId < @p4 and Resource.IsDeleted = 0 and Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            IndentedStringBuilder stringBuilder = new IndentedStringBuilder(new StringBuilder(startingString));
            SqlParameterCollection sqlParameterCollection = RuntimeHelpers.GetUninitializedObject(typeof(SqlParameterCollection)) as SqlParameterCollection;
            sqlParameterCollection.Add(new SqlParameter("@p1", 5L));
            sqlParameterCollection.Add(new SqlParameter("@p2", 4L));
            sqlParameterCollection.Add(new SqlParameter("@p3", 13L));
            sqlParameterCollection.Add(new SqlParameter("@p4", 11L));
            SqlCommandSimplifier.RemoveRedundantParameters(stringBuilder, sqlParameterCollection, _logger);
            string actualString = stringBuilder.ToString();
            Assert.Equal(expectedString, actualString);
        }

        [Fact]
        public void GivenACommandWithCTEs_WhenSimplified_ThenNothingIsChanged()
        {
            string startingString = "select distinct * from Resource where cte Resource.IsDeleted = 0 and Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            string expectedString = "select distinct * from Resource where cte Resource.IsDeleted = 0 and Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            IndentedStringBuilder stringBuilder = new IndentedStringBuilder(new StringBuilder(startingString));
            SqlParameterCollection sqlParameterCollection = RuntimeHelpers.GetUninitializedObject(typeof(SqlParameterCollection)) as SqlParameterCollection;
            SqlCommandSimplifier.RemoveRedundantParameters(stringBuilder, sqlParameterCollection, _logger);
            string actualString = stringBuilder.ToString();
            Assert.Equal(expectedString, actualString);
        }

        [Fact]
        public void GivenACommandWithOrConditions_WhenSimplified_ThenConditionsAreNotChanged()
        {
            string startingString = "select distinct * from Resource where Resource.ResourceSurrogateId >= @p1 and Resource.ResourceSurrogateId > @p2 and Resource.ResourceSurrogateId <= @p3 and Resource.ResourceSurrogateId < @p4 and Resource.IsDeleted = 0 or Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            string expectedString = "select * from Resource where Resource.ResourceSurrogateId >= @p1 and Resource.ResourceSurrogateId > @p2 and Resource.ResourceSurrogateId <= @p3 and Resource.ResourceSurrogateId < @p4 and Resource.IsDeleted = 0 or Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            IndentedStringBuilder stringBuilder = new IndentedStringBuilder(new StringBuilder(startingString));
            SqlParameterCollection sqlParameterCollection = RuntimeHelpers.GetUninitializedObject(typeof(SqlParameterCollection)) as SqlParameterCollection;
            sqlParameterCollection.Add(new SqlParameter("@p1", 5L));
            sqlParameterCollection.Add(new SqlParameter("@p2", 4L));
            sqlParameterCollection.Add(new SqlParameter("@p3", 13L));
            sqlParameterCollection.Add(new SqlParameter("@p4", 11L));
            SqlCommandSimplifier.RemoveRedundantParameters(stringBuilder, sqlParameterCollection, _logger);
            string actualString = stringBuilder.ToString();
            Assert.Equal(expectedString, actualString);
        }

        [Fact]
        public void GivenACommandThatFailsSimplification_WhenSimplified_ThenNothingIsChanged()
        {
            string startingString = "select distinct * from Resource where Resource.ResourceSurrogateId >= @p1 and Resource.ResourceSurrogateId > @p2 and Resource.IsDeleted = 0 and Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            string expectedString = "select distinct * from Resource where Resource.ResourceSurrogateId >= @p1 and Resource.ResourceSurrogateId > @p2 and Resource.IsDeleted = 0 and Resource.IsHistory = 1 order by Resource.ResourceSurrogateId desc";
            IndentedStringBuilder stringBuilder = new IndentedStringBuilder(new StringBuilder(startingString));
            SqlParameterCollection sqlParameterCollection = RuntimeHelpers.GetUninitializedObject(typeof(SqlParameterCollection)) as SqlParameterCollection;

            // The simplifier expects surrogate ids to be longs.
            sqlParameterCollection.Add(new SqlParameter("@p1", "test"));
            sqlParameterCollection.Add(new SqlParameter("@p2", 4L));
            SqlCommandSimplifier.RemoveRedundantParameters(stringBuilder, sqlParameterCollection, _logger);
            string actualString = stringBuilder.ToString();
            Assert.Equal(expectedString, actualString);
        }
    }
}
