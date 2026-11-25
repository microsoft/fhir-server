// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Caching)]
    public class DatabaseParametersTests
    {
        private readonly DatabaseParameters _parameters = new(new Dictionary<string, double>
        {
            ["Threshold"] = 2500,
            ["FlagOn"] = 1,
            ["FlagOff"] = 0,
            ["FlagOther"] = 2,
        });

        [Fact]
        public void GivenAStoredNumber_WhenRead_ThenItIsReturnedInsteadOfTheDefault()
        {
            Assert.Equal(2500, _parameters.GetNumber("Threshold", 5000));
            Assert.Equal(5000, _parameters.GetNumber("Missing", 5000));
        }

        [Theory]
        [InlineData("FlagOn", false, true)]
        [InlineData("FlagOff", true, false)]
        [InlineData("FlagOther", true, false)]
        [InlineData("Missing", true, true)]
        [InlineData("Missing", false, false)]
        public void GivenAFlag_WhenRead_ThenOnlyOneMeansEnabledAndMissingUsesTheDefault(string parameterId, bool defaultValue, bool expected)
        {
            Assert.Equal(expected, _parameters.IsEnabled(parameterId, defaultValue));
        }

        [Fact]
        public void GivenAnIdWithDifferentCaseOrTrailingSpaces_WhenRead_ThenItMatchesLikeTheSqlLookup()
        {
            var parameters = new DatabaseParameters(new Dictionary<string, double> { ["mergeresources.flag "] = 0 });

            Assert.False(parameters.IsEnabled("MergeResources.Flag", defaultValue: true));
        }

        [Fact]
        public void GivenTheEmptySettings_WhenRead_ThenEveryParameterUsesItsDefault()
        {
            Assert.Equal(5000, DatabaseParameters.Empty.GetNumber("Threshold", 5000));
            Assert.True(DatabaseParameters.Empty.IsEnabled("FlagOff", defaultValue: true));
        }
    }
}
