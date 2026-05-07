// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterSupportResolverTests : IAsyncLifetime
    {
        private SearchParameterSupportResolver _resolver;

        public async Task InitializeAsync()
        {
            _resolver = new SearchParameterSupportResolver(
                await SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync());
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void GivenASupportedSearchParameter_WhenResolvingSupport_ThenTrueIsReturned()
        {
            var sp = new SearchParameterInfo(
                "Condition-abatement-age",
                "Condition-abatement-age",
                SearchParamType.Quantity,
                new Uri("http://hl7.org/fhir/SearchParameter/Condition-abatement-age"),
                expression: "Condition.abatement.as(Range)",
                baseResourceTypes: new[] { "Condition" });

            var supported = _resolver.IsSearchParameterSupported(sp);

            Assert.True(supported.Supported);
            Assert.False(supported.IsPartiallySupported);
            Assert.False(supported.IsDateOnly);
            Assert.False(supported.IsScalarTemporal);
        }

        [Fact]
        public void GivenAnUnsupportedSearchParameter_WhenResolvingSupport_ThenFalseIsReturned()
        {
            var sp = new SearchParameterInfo(
                "Condition-abatement-age",
                "Condition-abatement-age",
                SearchParamType.Uri,
                new Uri("http://hl7.org/fhir/SearchParameter/Condition-abatement-age"),
                expression: "Condition.abatement.as(Range)",
                baseResourceTypes: new[] { "Condition" });

            // Can not convert Range => Uri
            var supported = _resolver.IsSearchParameterSupported(sp);

            Assert.False(supported.Supported);
            Assert.False(supported.IsPartiallySupported);
            Assert.False(supported.IsDateOnly);
            Assert.False(supported.IsScalarTemporal);
        }

        [Fact]
        public void GivenAPartiallySupportedSearchParameter_WhenResolvingSupport_ThenTrueIsReturned()
        {
            var sp = new SearchParameterInfo(
                "Condition-abatement-age",
                "Condition-abatement-age",
                SearchParamType.Quantity,
                new Uri("http://hl7.org/fhir/SearchParameter/Condition-abatement-age"),
#if Stu3 || R4 || R4B
                expression: "Condition.asserter | Condition.abatement.as(Range)",
#else
                expression: "Condition.participant.actor | Condition.abatement.as(Range)",
#endif
                baseResourceTypes: new[] { "Condition" });

            // Condition.asserter cannot be translated to Quantity
            // Condition.abatement.as(Range) CAN be translated to Quantity
            var supported = _resolver.IsSearchParameterSupported(sp);

            Assert.True(supported.Supported);
            Assert.True(supported.IsPartiallySupported);
            Assert.False(supported.IsDateOnly);
            Assert.False(supported.IsScalarTemporal);
        }

        [Fact]
        public void GivenASearchParameterWithBadType_WhenResolvingSupport_ThenFalseIsReturned()
        {
            var sp = new SearchParameterInfo(
                "Condition-abatement-age",
                "Condition-abatement-age",
                SearchParamType.Quantity,
                new Uri("http://hl7.org/fhir/SearchParameter/Condition-abatement-age"),
                expression: "Condition.asserter | Condition.abatement.as(Range)",
                baseResourceTypes: new[] { "UnknownType" });

            var supported = _resolver.IsSearchParameterSupported(sp);

            Assert.False(supported.Supported);
            Assert.False(supported.IsPartiallySupported);
            Assert.False(supported.IsDateOnly);
            Assert.False(supported.IsScalarTemporal);
        }

        [Fact]
        public void GivenASearchParameterWithNoBaseTypes_WhenResolvingSupport_ThenAnExceptionIsThrown()
        {
            var sp = new SearchParameterInfo(
                "Condition-abatement-age",
                "Condition-abatement-age",
                SearchParamType.Quantity,
                new Uri("http://hl7.org/fhir/SearchParameter/Condition-abatement-age"),
                expression: "Condition.asserter | Condition.abatement.as(Range)",
                baseResourceTypes: new string[0]);

            Assert.Throws<NotSupportedException>(() => _resolver.IsSearchParameterSupported(sp));
        }

        [Fact]
        public void GivenADateOnlyParameter_WhenResolvingSupport_ThenIsDateOnlyIsTrue()
        {
            var sp = new SearchParameterInfo(
                "Patient-birthdate",
                "birthdate",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" });

            var result = _resolver.IsSearchParameterSupported(sp);

            Assert.True(result.Supported);
            Assert.True(result.IsDateOnly);
            Assert.True(result.IsScalarTemporal);
        }

        [Fact]
        public void GivenADateTimeParameter_WhenResolvingSupport_ThenIsDateOnlyIsFalse()
        {
            // Observation.effective[x] resolves to dateTime / Period / Timing, never date-only.
            var sp = new SearchParameterInfo(
                "Observation-date",
                "date",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/clinical-date"),
                expression: "Observation.effective",
                baseResourceTypes: new[] { "Observation" });

            var result = _resolver.IsSearchParameterSupported(sp);

            Assert.True(result.Supported);
            Assert.False(result.IsDateOnly);
            Assert.False(result.IsScalarTemporal);
        }

        [Fact]
        public void GivenACustomAsDateParameter_WhenResolvingSupport_ThenIsDateOnlyIsTrue()
        {
            var sp = new SearchParameterInfo(
                "test-as-date",
                "test-as-date",
                SearchParamType.Date,
                new Uri("http://example.org/SearchParameter/test-as-date"),
                expression: "Patient.birthDate.as(date)",
                baseResourceTypes: new[] { "Patient" });

            var result = _resolver.IsSearchParameterSupported(sp);

            Assert.True(result.Supported);
            Assert.True(result.IsDateOnly);
            Assert.True(result.IsScalarTemporal);
        }

        [Fact]
        public void GivenANonDateParameter_WhenResolvingSupport_ThenIsDateOnlyIsFalse()
        {
            var sp = new SearchParameterInfo(
                "Patient-name",
                "name",
                SearchParamType.String,
                new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                expression: "Patient.name",
                baseResourceTypes: new[] { "Patient" });

            var result = _resolver.IsSearchParameterSupported(sp);

            Assert.True(result.Supported);
            Assert.False(result.IsDateOnly);
            Assert.False(result.IsScalarTemporal);
        }

        [Fact]
        public void GivenAScalarDateTimeParameter_WhenResolvingSupport_ThenIsScalarTemporalIsTrue()
        {
            var sp = new SearchParameterInfo(
                "MedicationRequest-authoredon",
                "authoredon",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/MedicationRequest-authoredon"),
                expression: "MedicationRequest.authoredOn",
                baseResourceTypes: new[] { "MedicationRequest" });

            var result = _resolver.IsSearchParameterSupported(sp);

            Assert.True(result.Supported);
            Assert.False(result.IsDateOnly);
            Assert.True(result.IsScalarTemporal);
        }

        [Fact]
        public void GivenAnInstantParameter_WhenResolvingSupport_ThenIsScalarTemporalIsTrue()
        {
            var sp = new SearchParameterInfo(
                "Bundle-timestamp",
                "timestamp",
                SearchParamType.Date,
                new Uri("http://example.org/SearchParameter/Bundle-timestamp"),
                expression: "Bundle.timestamp",
                baseResourceTypes: new[] { "Bundle" });

            var result = _resolver.IsSearchParameterSupported(sp);

            Assert.True(result.Supported);
            Assert.False(result.IsDateOnly);
            Assert.True(result.IsScalarTemporal);
        }

        [Fact]
        public void GivenAPeriodParameter_WhenResolvingSupport_ThenIsScalarTemporalIsFalse()
        {
            var sp = new SearchParameterInfo(
                "Encounter-date",
                "date",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/clinical-date"),
                expression: "Encounter.period",
                baseResourceTypes: new[] { "Encounter" });

            var result = _resolver.IsSearchParameterSupported(sp);

            Assert.True(result.Supported);
            Assert.False(result.IsDateOnly);
            Assert.False(result.IsScalarTemporal);
        }

        [Fact]
        public void GivenAMixedTemporalParameter_WhenResolvingSupport_ThenIsScalarTemporalIsFalse()
        {
            var sp = new SearchParameterInfo(
                "Condition-onset-date",
                "date",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/clinical-date"),
                expression: "Condition.onset",
                baseResourceTypes: new[] { "Condition" });

            var result = _resolver.IsSearchParameterSupported(sp);

            Assert.True(result.Supported);
            Assert.False(result.IsDateOnly);
            Assert.False(result.IsScalarTemporal);
        }

        [Fact]
        public void GivenACompositeDateParameter_WhenResolvingSupport_ThenIsScalarTemporalIsFalse()
        {
            var birthdate = new SearchParameterInfo(
                "birthdate",
                "birthdate",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" });

            var composite = new SearchParameterInfo(
                "Patient-code-birthdate",
                "code-birthdate",
                SearchParamType.Composite,
                new Uri("http://example.org/SearchParameter/Patient-code-birthdate"),
                components: new[]
                {
                    new SearchParameterComponentInfo(
                        new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                        "birthDate")
                    {
                        ResolvedSearchParameter = birthdate,
                    },
                },
                expression: "Patient",
                baseResourceTypes: new[] { "Patient" });

            var result = _resolver.IsSearchParameterSupported(composite);

            Assert.True(result.Supported);
            Assert.False(result.IsDateOnly);
            Assert.False(result.IsScalarTemporal);
        }
    }
}
