// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class HashingSqlQueryParameterManagerTests
    {
        private const int DefaultMaxResourceIdLength = 64;
        private const int ExtendedMaxResourceIdLength = 128;

        public static readonly TheoryData<object> Data = new()
        {
            true, 1, 1L, DateTime.UtcNow, DateTimeOffset.UtcNow, 9M, 99.9, (short)6, (byte)9, Guid.Parse("0fd465f0-095b-425c-a3e8-acc879d20835"), "Hello",
        };

        [Fact]
        public void GivenParametersThatShouldNotBeHashed_WhenAdded_ResultsInNoChangeToHash()
        {
            using var command = new SqlCommand();
            var parameters = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters));

            AssertDoesNotChangeHash(parameters, () =>
            {
                parameters.AddParameter(1, includeInHash: false);
                parameters.AddParameter(VLatest.Resource.ResourceId, "abc", false);
                parameters.AddParameter(VLatest.Resource.ResourceId, (object)"123", false);
                parameters.AddParameter(VLatest.Resource.ResourceSurrogateId, 123, false);
            });

            Assert.False(parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenParameterThatShouldBeHashed_WhenAdded_ChangesHash()
        {
            using var command = new SqlCommand();
            var parameters = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters));

            AssertChangesHash(parameters, () =>
            {
                parameters.AddParameter(VLatest.Resource.ResourceSurrogateId, 123, true);
            });

            Assert.True(parameters.HasParametersToHash);
        }

        [Theory]
        [MemberData(nameof(Data))]
        public void GivenAParameterThatShouldBeHashed_WhenAdded_ChangesHash(object value)
        {
            using var command = new SqlCommand();
            var parameters = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters));

            AssertChangesHash(parameters, () => parameters.AddParameter(value, true));
        }

        [Fact]
        public void GivenAParameterThatShouldAndThenShouldNotBeHashed_WhenAdded_ChangesHash()
        {
            using var command = new SqlCommand();
            var parameters = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters));

            AssertChangesHash(parameters, () =>
            {
                parameters.AddParameter(1, includeInHash: false);
                parameters.AddParameter(1, includeInHash: true);
            });

            Assert.True(parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenAParameterThatShouldNotAndThenShouldBeHashed_WhenAdded_ChangesHash()
        {
            using var command = new SqlCommand();
            var parameters = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters));

            AssertChangesHash(parameters, () =>
            {
                parameters.AddParameter(1, includeInHash: true);
                parameters.AddParameter(1, includeInHash: false);
            });

            Assert.True(parameters.HasParametersToHash);
        }

        [Fact]
        public void GivenALargeNumberOfParameters_WhenAdded_ChangesHash()
        {
            using var command = new SqlCommand();
            var parameters = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters));

            for (int i = 0; i < 100; i++)
            {
                AssertChangesHash(parameters, () =>
                {
                    parameters.AddParameter(Guid.NewGuid(), true);
                });

                Assert.True(parameters.HasParametersToHash);
            }

            // ensure hash is repeatable with IncrementalHash
            Assert.Equal(GetHash(parameters), GetHash(parameters));
        }

        [Fact]
        public void GivenALargeStringParameter_WhenAdded_ChangesHash()
        {
            using var command = new SqlCommand();
            var parameters = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters));

            parameters.AddParameter(1, true);

            AssertChangesHash(parameters, () => parameters.AddParameter(new string('a', 500), true));
        }

        [Theory]
        [InlineData("ResourceId")]
        [InlineData("ReferenceResourceId")]
        [InlineData("ReferenceResourceId1")]
        public void GivenAResourceIdColumn_WhenAddedWithTheDefaultMaxResourceIdLength_ThenTheParameterKeepsTheSchemaWidth(string columnName)
        {
            // Arrange
            using var command = new SqlCommand();
            var parameters = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters));

            // Act
            var parameter = (SqlParameter)parameters.AddParameter(GetResourceIdColumn(columnName), "abc", includeInHash: false);

            // Assert
            Assert.Equal(SqlDbType.VarChar, parameter.SqlDbType);
            Assert.Equal(DefaultMaxResourceIdLength, parameter.Size);
        }

        [Theory]
        [InlineData("ResourceId")]
        [InlineData("ReferenceResourceId")]
        [InlineData("ReferenceResourceId1")]
        public void GivenAResourceIdColumn_WhenAddedWithAConfiguredMaxResourceIdLength_ThenTheParameterIsWidened(string columnName)
        {
            // Arrange
            using var command = new SqlCommand();
            var parameters = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters), ExtendedMaxResourceIdLength);
            var value = new string('a', ExtendedMaxResourceIdLength);

            // Act
            var parameter = (SqlParameter)parameters.AddParameter(GetResourceIdColumn(columnName), value, includeInHash: false);

            // Assert
            Assert.Equal(SqlDbType.VarChar, parameter.SqlDbType);
            Assert.Equal(ExtendedMaxResourceIdLength, parameter.Size);
            Assert.Equal(value, parameter.Value);
        }

        [Fact]
        public void GivenAResourceIdAndANarrowVarCharColumnWithTheSameValue_WhenAdded_ThenDistinctParametersAreCreated()
        {
            // Arrange - the inner manager de-duplicates on (type, length, value), so widening has to happen
            // before the parameter is created. Resizing an already-created/shared parameter would collapse
            // these two columns onto a single parameter and silently narrow or widen the other one.
            const string value = "abc";
            using var command = new SqlCommand();
            var parameters = new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters), ExtendedMaxResourceIdLength);

            // Act
            var searchParamHashParameter = (SqlParameter)parameters.AddParameter(VLatest.Resource.SearchParamHash, value, includeInHash: false);
            var resourceIdParameter = (SqlParameter)parameters.AddParameter(VLatest.Resource.ResourceId, value, includeInHash: false);

            // Assert
            Assert.NotSame(searchParamHashParameter, resourceIdParameter);
            Assert.Equal(SqlDbType.VarChar, searchParamHashParameter.SqlDbType);
            Assert.Equal(DefaultMaxResourceIdLength, searchParamHashParameter.Size);
            Assert.Equal(ExtendedMaxResourceIdLength, resourceIdParameter.Size);
            Assert.Equal(2, command.Parameters.Count);
        }

        private static Column GetResourceIdColumn(string columnName) => columnName switch
        {
            "ResourceId" => VLatest.Resource.ResourceId,
            "ReferenceResourceId" => VLatest.ReferenceSearchParam.ReferenceResourceId,
            "ReferenceResourceId1" => VLatest.ReferenceTokenCompositeSearchParam.ReferenceResourceId1,
            _ => throw new ArgumentOutOfRangeException(nameof(columnName)),
        };

        private static string GetHash(HashingSqlQueryParameterManager parameterManager)
        {
            var sb = new IndentedStringBuilder(new StringBuilder());
            parameterManager.AppendHash(sb);
            return sb.ToString();
        }

        private static void AssertChangesHash(HashingSqlQueryParameterManager parameters, Action action)
        {
            var originalHash = GetHash(parameters);

            action();

            Assert.NotEqual(originalHash, GetHash(parameters));
        }

        private static void AssertDoesNotChangeHash(HashingSqlQueryParameterManager parameters, Action action)
        {
            var originalHash = GetHash(parameters);

            action();

            Assert.Equal(originalHash, GetHash(parameters));
        }
    }
}
