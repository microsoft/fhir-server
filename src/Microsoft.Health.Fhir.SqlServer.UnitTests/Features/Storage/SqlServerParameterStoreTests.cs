// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Parameters;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    /// <summary>
    /// Unit tests for SqlServerParameterStore.
    /// Tests the explicit behavior of SetParameter (throws NotImplementedException).
    /// Note: GetParameter and cache logic tests require database access and are covered by integration tests.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerParameterStoreTests
    {
        private readonly ISqlConnectionBuilder _sqlConnectionBuilder;
        private readonly ILogger<SqlServerParameterStore> _logger;

        public SqlServerParameterStoreTests()
        {
            _sqlConnectionBuilder = Substitute.For<ISqlConnectionBuilder>();
            _logger = Substitute.For<ILogger<SqlServerParameterStore>>();
        }

        /// <summary>
        /// Tests that SetParameter throws NotImplementedException as expected.
        /// This is the documented behavior since parameters are only read, not written, in SQL Server implementation.
        /// </summary>
        [Fact]
        public async Task GivenParameter_WhenSetParameterCalled_ThenThrowsNotImplementedException()
        {
            // Arrange
            var store = new SqlServerParameterStore(_sqlConnectionBuilder, _logger);
            var parameter = new Parameter
            {
                Name = "TestParameter",
                CharValue = "TestValue",
                NumberValue = 123.45,
                LongValue = 9876543210L,
                DateValue = DateTime.UtcNow,
                BooleanValue = true,
                UpdatedOn = DateTime.UtcNow,
                UpdatedBy = "TestUser",
            };

            // Act & Assert
            await Assert.ThrowsAsync<NotImplementedException>(
                async () => await store.SetParameter(parameter, CancellationToken.None));
        }
    }
}
