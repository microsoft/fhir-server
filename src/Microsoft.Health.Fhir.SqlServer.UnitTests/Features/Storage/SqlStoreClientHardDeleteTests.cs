// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    /// <summary>
    /// Regressions for the hard delete resource id length guard. Stored procedures that predate configurable
    /// resource id lengths declare <c>@ResourceId</c> as <c>varchar(64)</c> and would silently truncate, so ids
    /// longer than 64 characters must carry an extra <c>@ExpectedResourceIdLength</c> parameter that those
    /// procedures reject.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlStoreClientHardDeleteTests
    {
        [Fact]
        public async Task GivenAResourceIdLongerThanTheSchemaDefault_WhenHardDeleting_ThenTheExpectedLengthGuardIsSent()
        {
            // Arrange
            string resourceId = new string('a', 100);
            var capturedParameters = new List<KeyValuePair<string, object>>();
            ISqlRetryService sqlRetryService = CreateRetryService(capturedParameters);
            SqlStoreClient client = CreateClient(sqlRetryService, maxResourceIdLength: 128);

            // Act
            await client.HardDeleteAsync(1, resourceId, keepCurrentVersion: false, isResourceChangeCaptureEnabled: false, CancellationToken.None);

            // Assert
            Assert.Equal(resourceId, GetParameterValue(capturedParameters, "@ResourceId"));
            Assert.Equal(resourceId.Length, GetParameterValue(capturedParameters, "@ExpectedResourceIdLength"));
        }

        [Fact]
        public async Task GivenAResourceIdWithinTheSchemaDefault_WhenHardDeleting_ThenNoExpectedLengthGuardIsSent()
        {
            // Arrange
            string resourceId = new string('a', 64);
            var capturedParameters = new List<KeyValuePair<string, object>>();
            ISqlRetryService sqlRetryService = CreateRetryService(capturedParameters);
            SqlStoreClient client = CreateClient(sqlRetryService, maxResourceIdLength: 128);

            // Act
            await client.HardDeleteAsync(1, resourceId, keepCurrentVersion: false, isResourceChangeCaptureEnabled: false, CancellationToken.None);

            // Assert
            Assert.Equal(resourceId, GetParameterValue(capturedParameters, "@ResourceId"));
            Assert.DoesNotContain(capturedParameters, p => string.Equals(p.Key, "@ExpectedResourceIdLength", StringComparison.Ordinal));
        }

        private static ISqlRetryService CreateRetryService(List<KeyValuePair<string, object>> capturedParameters)
        {
            var sqlRetryService = Substitute.For<ISqlRetryService>();

            sqlRetryService.ExecuteSql(
                Arg.Do<SqlCommand>(cmd => capturedParameters.AddRange(cmd.Parameters.Cast<SqlParameter>().Select(p => new KeyValuePair<string, object>(p.ParameterName, p.Value)))),
                Arg.Any<Func<SqlCommand, CancellationToken, Task>>(),
                Arg.Any<ILogger>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<string>())
                .Returns(Task.CompletedTask);

            return sqlRetryService;
        }

        private static SqlStoreClient CreateClient(ISqlRetryService sqlRetryService, int maxResourceIdLength)
        {
            return new SqlStoreClient(
                sqlRetryService,
                NullLogger<SqlStoreClient>.Instance,
                new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max),
                Options.Create(new CoreFeatureConfiguration { MaxResourceIdLength = maxResourceIdLength }));
        }

        private static object GetParameterValue(List<KeyValuePair<string, object>> capturedParameters, string name)
        {
            return capturedParameters.SingleOrDefault(p => string.Equals(p.Key, name, StringComparison.Ordinal)).Value;
        }
    }
}
