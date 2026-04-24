// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Models
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterInfoIsDateOnlyTests
    {
        [Fact]
        public void GivenANewSearchParameterInfo_WhenConstructed_ThenIsDateOnlyDefaultsToFalse()
        {
            var sp = new SearchParameterInfo(
                "birthdate",
                "birthdate",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" });

            Assert.False(sp.IsDateOnly);
        }

        [Fact]
        public void GivenSearchParameterInfo_WhenIsDateOnlyToggles_ThenCalculateSearchParameterHashIsUnchanged()
        {
            var sp = new SearchParameterInfo(
                "birthdate",
                "birthdate",
                SearchParamType.Date,
                new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                expression: "Patient.birthDate",
                baseResourceTypes: new[] { "Patient" })
            {
                SearchParameterStatus = SearchParameterStatus.Enabled,
            };

            string hashBefore = new List<SearchParameterInfo> { sp }.CalculateSearchParameterHash();

            sp.IsDateOnly = true;

            string hashAfter = new List<SearchParameterInfo> { sp }.CalculateSearchParameterHash();

            Assert.Equal(hashBefore, hashAfter);
        }
    }
}
