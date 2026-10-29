// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs.QueryStoreDiagnostics
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class QueryStoreDiagnosticsConfigurationPrecedenceTests
    {
        [Fact]
        public async Task GivenConfiguredPeriods_WhenStartingTheWatchdog_ThenItUsesThemWithoutReadingOrWritingDatabaseParameters()
        {
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            var configuration = new WatchdogConfiguration();
            configuration.QueryStoreDiagnostics.Enabled = true;
            configuration.QueryStoreDiagnostics.PeriodSec = 120;
            configuration.QueryStoreDiagnostics.LeasePeriodSec = 240;
            var watchdog = new QueryStoreDiagnosticsWatchdog(
                sqlRetryService,
                NullLogger<QueryStoreDiagnosticsWatchdog>.Instance,
                Options.Create(configuration),
                new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = SchemaVersionConstants.Max });
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watchdog.ExecuteAsync(Guid.NewGuid(), cancellation.Token));

            Assert.True(watchdog.IsInitialized);
            Assert.Equal(120, watchdog.PeriodSec);
            Assert.Equal(240, watchdog.LeasePeriodSec);
            Assert.Empty(sqlRetryService.ReceivedCalls());
        }
    }
}
