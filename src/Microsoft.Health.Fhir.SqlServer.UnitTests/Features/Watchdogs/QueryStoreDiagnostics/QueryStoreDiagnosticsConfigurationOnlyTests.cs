// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs.QueryStoreDiagnostics
{
    /// <summary>
    /// Pins the property the feature is required to have: it is configured entirely through configuration and never
    /// reads or writes <c>dbo.Parameters</c>. Both ways that property could be lost are silent — re-deriving from
    /// <see cref="Watchdog{T}"/> reintroduces the seeding insert without a line of code being written in this
    /// feature, and a hand-written statement is only ever exercised against a live database — so neither is caught
    /// by the rest of the unit suite.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class QueryStoreDiagnosticsConfigurationOnlyTests
    {
        [Fact]
        public void GivenTheWatchdog_WhenItsTypeIsInspected_ThenItDoesNotInheritTheParameterSeedingBaseClass()
        {
            // Arrange, Act
            Type baseType = typeof(QueryStoreDiagnosticsWatchdog).BaseType;

            // Assert
            // Watchdog<T>.ExecuteAsync unconditionally awaits a private, non-virtual InitParamsAsync that inserts
            // {Name}.PeriodSec and {Name}.LeasePeriodSec into dbo.Parameters and then reads the period back over the
            // configured one. There is no hook to suppress it, so not deriving from it is the mechanism by which
            // this feature writes nothing, and re-deriving would undo that without touching this feature's code.
            Assert.Equal(typeof(object), baseType);
        }

        [Fact]
        public void GivenEveryStatementTheWatchdogCanIssue_WhenInspected_ThenNoneReadsOrWritesDboParameters()
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
            Assert.NotEmpty(statements);
            Assert.All(statements, statement => Assert.DoesNotContain("dbo.Parameters", statement, StringComparison.OrdinalIgnoreCase));
        }
    }
}
