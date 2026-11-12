// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlStoreClientTests
    {
        private const short PatientTypeId = 103;
        private const string HardDelete = nameof(SqlStoreClient.HardDeleteAsync);
        private const string Get = nameof(SqlStoreClient.GetAsync);
        private const string GetVersions = nameof(SqlStoreClient.GetResourceVersionsAsync);

        private readonly ISqlRetryService _sqlRetryService = Substitute.For<ISqlRetryService>();

        /// <summary>
        /// Gets the root-id operation, active schema version, and id length for ids the active root columns store whole.
        /// </summary>
        public static TheoryData<string, int?, int> FittingIds => new TheoryData<string, int?, int>
        {
            { HardDelete, (int)SchemaVersion.V117, 64 },
            { Get, (int)SchemaVersion.V117, 64 },
            { GetVersions, (int)SchemaVersion.V117, 64 },
            { HardDelete, SchemaVersionConstants.Max, 128 },
            { Get, SchemaVersionConstants.Max, 128 },
            { GetVersions, SchemaVersionConstants.Max, 128 },
        };

        /// <summary>
        /// Gets the root-id operation, active schema version, id length, and active root column capacity for ids the schema would shorten.
        /// </summary>
        public static TheoryData<string, int?, int, int> OverCapacityIds => new TheoryData<string, int?, int, int>
        {
            { HardDelete, (int)SchemaVersion.V117, 65, 64 },
            { Get, (int)SchemaVersion.V117, 65, 64 },
            { GetVersions, (int)SchemaVersion.V117, 65, 64 },
            { HardDelete, (int)SchemaVersion.V117, 128, 64 },
            { HardDelete, null, 65, 64 },
            { HardDelete, SchemaVersionConstants.Max, 129, 128 },
        };

        [Theory]
        [MemberData(nameof(FittingIds))]
        public async Task GivenAnIdThatFitsTheActiveRootColumn_WhenTheStoreUsesIt_ThenTheCommandIsSent(string operation, int? currentSchemaVersion, int length)
        {
            // Arrange
            SqlStoreClient client = CreateClient(currentSchemaVersion);

            // Act
            await RunAsync(client, operation, CreateId(length));

            // Assert
            Assert.NotEmpty(_sqlRetryService.ReceivedCalls());
        }

        [Theory]
        [MemberData(nameof(OverCapacityIds))]
        public async Task GivenAnIdLongerThanTheActiveRootColumn_WhenTheStoreUsesIt_ThenAValueFreeBadRequestIsThrownBeforeAnyCommandIsSent(string operation, int? currentSchemaVersion, int length, int capacity)
        {
            // Arrange
            SqlStoreClient client = CreateClient(currentSchemaVersion);
            string resourceId = CreateId(length);

            // Act
            var exception = await Assert.ThrowsAsync<BadRequestException>(() => RunAsync(client, operation, resourceId));

            // Assert
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, Microsoft.Health.Fhir.Core.Resources.IdRequirements, capacity), exception.Message);
            Assert.DoesNotContain(resourceId[..capacity], exception.Message, StringComparison.Ordinal);
            Assert.Empty(_sqlRetryService.ReceivedCalls());
        }

        [Fact]
        public async Task GivenTheV118Schema_WhenHardDeletingA128CharacterId_ThenTheWholeIdIsBound()
        {
            // Arrange
            string boundId = null;
            _sqlRetryService
                .ExecuteSql(
                    Arg.Do<SqlCommand>(cmd => boundId = (string)cmd.Parameters["@ResourceId"].Value),
                    Arg.Any<Func<SqlCommand, CancellationToken, Task>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>(),
                    Arg.Any<bool>(),
                    Arg.Any<string>())
                .Returns(Task.CompletedTask);
            SqlStoreClient client = CreateClient((int)SchemaVersion.V118);
            string resourceId = CreateId(128);

            // Act
            await client.HardDeleteAsync(PatientTypeId, resourceId, keepCurrentVersion: false, isResourceChangeCaptureEnabled: false, CancellationToken.None);

            // Assert
            Assert.Equal(resourceId, boundId);
        }

        private SqlStoreClient CreateClient(int? currentSchemaVersion)
            => new SqlStoreClient(
                _sqlRetryService,
                NullLogger<SqlStoreClient>.Instance,
                new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = currentSchemaVersion });

        // Ids longer than 64 characters start with the same 64 characters as the 64-character id, so a narrowed id would address that stored row.
        private static string CreateId(int length) => new string('a', Math.Min(length, 64)) + new string('b', Math.Max(length - 64, 0));

        private static async Task RunAsync(SqlStoreClient client, string operation, string resourceId)
        {
            switch (operation)
            {
                case HardDelete:
                    await client.HardDeleteAsync(PatientTypeId, resourceId, keepCurrentVersion: false, isResourceChangeCaptureEnabled: false, CancellationToken.None);
                    break;
                case Get:
                    await client.GetAsync(new[] { new ResourceDateKey(PatientTypeId, resourceId, 0, null) }, _ => null, _ => "Patient", isReadOnly: true, CancellationToken.None);
                    break;
                case GetVersions:
                    await client.GetResourceVersionsAsync(new[] { new ResourceDateKey(PatientTypeId, resourceId, 0, null) }, _ => null, CancellationToken.None);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
    }
}
