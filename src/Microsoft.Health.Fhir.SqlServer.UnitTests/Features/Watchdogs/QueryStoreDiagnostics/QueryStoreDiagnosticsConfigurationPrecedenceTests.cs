// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs.QueryStoreDiagnostics
{
    /// <summary>
    /// Pins the invariant the feature is now required to have: configuration is authoritative over the two
    /// <c>dbo.Parameters</c> rows the <see cref="Watchdog{T}"/> base class seeds for it. The base class seeds those
    /// rows and then reads them back over the configured values, and <c>dbo.Parameters</c> carries
    /// <c>IGNORE_DUP_KEY = ON</c>, so on a database that already holds the rows the seeding insert is a silent no-op
    /// and a stale row would win over an environment variable. The only thing that stops that is the overridden
    /// <see cref="Watchdog{T}.InitAdditionalParamsAsync"/>, which reconciles the rows back to configuration with an
    /// <c>UPDATE</c>. Every way that override could be lost is silent — deleting it, or "fixing" the reconciliation
    /// into an <c>INSERT</c> that <c>IGNORE_DUP_KEY</c> no-ops, compiles and passes every other unit test, because
    /// the reconciliation is only ever exercised against a live database — so these reflection assertions are what
    /// catch it.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class QueryStoreDiagnosticsConfigurationPrecedenceTests
    {
        [Fact]
        public void GivenTheWatchdog_WhenItsTypeIsInspected_ThenItDerivesFromTheWatchdogBaseClass()
        {
            // Arrange, Act
            Type baseType = typeof(QueryStoreDiagnosticsWatchdog).BaseType;

            // Assert
            // Deriving from Watchdog<T> is what supplies the timer and the single-replica lease, and with them the
            // seeding of {Name}.PeriodSec and {Name}.LeasePeriodSec into dbo.Parameters. Accepting those rows is
            // deliberate; keeping configuration authoritative over them is the job of the InitAdditionalParamsAsync
            // override asserted below.
            Assert.Equal(typeof(Watchdog<QueryStoreDiagnosticsWatchdog>), baseType);
        }

        [Fact]
        public void GivenTheWatchdog_WhenInitAdditionalParamsIsInspected_ThenItIsOverriddenOnTheWatchdog()
        {
            // Arrange, Act
            MethodInfo initAdditionalParams = typeof(QueryStoreDiagnosticsWatchdog).GetMethod(
                "InitAdditionalParamsAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);

            // Assert
            // The base class's InitParamsAsync reads the stored period and lease period back over the configured
            // values, then calls this hook and only afterwards builds the timer. Overriding it here is the ONLY
            // place configuration is reasserted, so if the declaring type were the base class — the override
            // deleted — the stale-row bug would return with no other test failing.
            Assert.NotNull(initAdditionalParams);
            Assert.Equal(typeof(QueryStoreDiagnosticsWatchdog), initAdditionalParams.DeclaringType);
        }

        [Fact]
        public void GivenTheReconciliationStatement_WhenInspected_ThenItUpdatesDboParametersAndDoesNotInsert()
        {
            // Arrange
            // The exact statement the override issues, read from the type rather than duplicated, so this fails if the
            // real reconciliation ever stops being an UPDATE against dbo.Parameters.
            string reconciliationSql = QueryStoreDiagnosticsWatchdog.ReconcileParametersSql;

            // Act, Assert
            Assert.Contains("dbo.Parameters", reconciliationSql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("UPDATE", reconciliationSql, StringComparison.OrdinalIgnoreCase);

            // An INSERT here would be silently no-op'd by IGNORE_DUP_KEY on an existing database, leaving the stale
            // row in place and reintroducing the bug the override exists to close.
            Assert.DoesNotContain("INSERT", reconciliationSql, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GivenEveryConstStatementTheWatchdogCanIssue_WhenInspected_ThenNoneInsertsIntoDboParameters()
        {
            // Arrange
            // Every statement this watchdog issues is a const string on the type, so taking all of its string
            // literals is a superset of its SQL — the few non-SQL literals caught alongside them cost nothing.
            string[] statements = typeof(QueryStoreDiagnosticsWatchdog)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue())
                .ToArray();

            // Act, Assert
            // The single UPDATE the reconciliation issues is the whole of what this feature writes to dbo.Parameters;
            // no statement inserts into it, which is what keeps IGNORE_DUP_KEY from ever silently ignoring a write
            // this feature depended on taking effect.
            Assert.NotEmpty(statements);
            Assert.All(
                statements,
                statement => Assert.DoesNotContain("INSERT INTO dbo.Parameters", statement, StringComparison.OrdinalIgnoreCase));
        }
    }
}
