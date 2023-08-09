// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class HashingSqlQueryParameterManagerTests
    {
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
                parameters.AddParameter(VLatest.ResourceCurrent.ResourceId, "abc", false);
                parameters.AddParameter(VLatest.ResourceCurrent.ResourceId, (object)"123", false);
            });

            Assert.False(parameters.HasParametersToHash);
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
