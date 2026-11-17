// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Integration.Features.Search.SqlInstantDateTimeSearchTests;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Search
{
    [Collection("Instant date clock")]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlCalendarDateSqlGenerationTests : IClassFixture<InstantSearchFixture>
    {
        private readonly InstantSearchFixture _fixture;

        public SqlCalendarDateSqlGenerationTests(InstantSearchFixture fixture)
        {
            _fixture = fixture;
        }

        [Theory]
        [InlineData("ClaimResponse", "payment-date", TemporalType.Date)]
#if R5
        [InlineData("Basic", "created", TemporalType.DateTime)]
#else
        [InlineData("Basic", "created", TemporalType.Date)]
#endif
        [InlineData("Patient", "birthdate", TemporalType.Date)]
        [InlineData("RelatedPerson", "birthdate", TemporalType.Date)]
        [InlineData("Composition", "date", TemporalType.Unknown)]
        [InlineData("Slot", "start", TemporalType.Instant)]
        [InlineData("Schedule", "date", TemporalType.Period)]
        [InlineData("Observation", "date", TemporalType.Unknown)]
        public void GivenBuiltInTemporalLeaves_WhenExactDaySqlIsGenerated_ThenOnlyCalendarDatesUseEndEquality(
            string resourceType,
            string code,
            TemporalType expectedType)
        {
            var definition = _fixture.Definitions.GetSearchParameter(resourceType, code);
            Assert.True(definition.IsSystemDefined);
            Assert.Equal(expectedType, definition.TemporalType);
            if (code == "birthdate")
            {
                Assert.Contains("|", definition.Expression);
            }

            string url = $"{resourceType}?{code}=2016-07-06";
            using var legacy = _fixture.Generate(url, instant: false);
            using var optimized = _fixture.Generate(url, instant: false, scalar: true);

            if (expectedType == TemporalType.Date)
            {
                Assert.Matches(@"EndDateTime\s*=\s*@\w+", optimized.CommandText);
                Assert.Contains("IsLongerThanADay", optimized.CommandText);
                Assert.Matches(@"StartDateTime\s*<=\s*@\w+", optimized.CommandText);
                Assert.Matches(@"EndDateTime\s*>=\s*@\w+", optimized.CommandText);
                Assert.DoesNotContain("UNION ALL", optimized.CommandText, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                Assert.Equal(legacy.CommandText, optimized.CommandText);
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void GivenClassificationAndScalarFlags_WhenExactDaySqlIsGenerated_ThenBothAreRequired(
            bool classification,
            bool scalar)
        {
            var fixture = new InstantSearchFixture(classification);
            const string url = "ClaimResponse?payment-date=2016-07-06";
            using var command = fixture.Generate(url, instant: false, containment: true, scalar: scalar);

            Assert.Equal(classification && scalar, command.CommandText.Contains("IsLongerThanADay", StringComparison.Ordinal));
            if (classification && scalar)
            {
                Assert.Matches(@"EndDateTime\s*=\s*@\w+", command.CommandText);
                Assert.DoesNotContain("StartDateTime", command.CommandText);
            }
        }

        [Theory]
        [InlineData("Patient", "birthdate")]
        [InlineData("ClaimResponse", "payment-date")]
        public void GivenClassificationDisabled_WhenCalendarDateSqlIsGenerated_ThenScalarFlagDoesNotChangeSql(string resourceType, string code)
        {
            var fixture = new InstantSearchFixture(classificationEnabled: false);
            string url = $"{resourceType}?{code}=2016-07-06";

            using var legacy = fixture.Generate(url, instant: false);
            using var scalar = fixture.Generate(url, instant: false, scalar: true);

            Assert.Equal(TemporalType.Unknown, fixture.Definitions.GetSearchParameter(resourceType, code).TemporalType);
            Assert.Equal(legacy.CommandText, scalar.CommandText);
        }

        [Theory]
        [InlineData("2016-07-06")]
        [InlineData("eq2016-07-06")]
        public void GivenCustomDateDefinition_WhenExactDaySqlIsGenerated_ThenScalarFlagDoesNotChangeSql(string value)
        {
            var definition = _fixture.Definitions.GetSearchParameter("Patient", "custom-date");
            Assert.False(definition.IsSystemDefined);
            Assert.Equal(TemporalType.Unknown, definition.TemporalType);
            string url = $"Patient?custom-date={value}";

            using var legacy = _fixture.Generate(url, instant: false);
            using var scalar = _fixture.Generate(url, instant: false, scalar: true);

            Assert.Equal(legacy.CommandText, scalar.CommandText);
        }

        [Theory]
        [InlineData("2016")]
        [InlineData("2016-07")]
        [InlineData("2016-07-06T12:00:00Z")]
        [InlineData("ge2016-07-06")]
        [InlineData("lt2016-07-06")]
        [InlineData("ne2016-07-06")]
        [InlineData("ap2016-07-06")]
        public void GivenNonExactDayQuery_WhenCalendarDateSqlIsGenerated_ThenScalarFlagDoesNotChangeSql(string value)
        {
            using var clock = Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(InstantSearchFixture.FrozenNow));
            string url = $"ClaimResponse?payment-date={value}";
            using var legacy = _fixture.Generate(url, instant: false);
            using var scalar = _fixture.Generate(url, instant: false, scalar: true);

            Assert.Equal(legacy.CommandText, scalar.CommandText);
        }
    }
}
