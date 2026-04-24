// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchParameters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterToTypeResolverDateMappingTests
    {
        [Fact]
        public void GivenAnAsDateExpression_WhenResolved_ThenFhirNodeTypeIsDate()
        {
            // Mirrors a custom search parameter that uses .as(date) string-cast syntax.
            var parsed = new FhirPathCompiler().Parse("Patient.birthDate.as(date)");

            SearchParameterTypeResult[] results = SearchParameterToTypeResolver
                .Resolve(
                    "Patient",
                    (SearchParamType.Date, parsed, new Uri("http://example.org/SearchParameter/test-date")),
                    componentExpressions: null)
                .ToArray();

            Assert.NotEmpty(results);
            Assert.All(results, r => Assert.Equal("date", r.FhirNodeType, ignoreCase: true));
        }

        [Fact]
        public void GivenAnAsDateTimeExpression_WhenResolved_ThenFhirNodeTypeIsDateTime()
        {
            var parsed = new FhirPathCompiler().Parse("Observation.effective.as(dateTime)");

            SearchParameterTypeResult[] results = SearchParameterToTypeResolver
                .Resolve(
                    "Observation",
                    (SearchParamType.Date, parsed, new Uri("http://example.org/SearchParameter/test-datetime")),
                    componentExpressions: null)
                .ToArray();

            Assert.NotEmpty(results);
            Assert.All(results, r => Assert.Equal("dateTime", r.FhirNodeType, ignoreCase: true));
        }
    }
}
