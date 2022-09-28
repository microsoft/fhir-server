// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage.Search
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SortValueJObjectGeneratorTests
    {
        private SortValueJObjectGenerator _generator = new();

        [Theory]
        [InlineData("Muller", "MULLER")]
        [InlineData("Müller", "MULLER")]
        [InlineData("Circled①", "CIRCLED1")]
        [InlineData("Super⁹", "SUPER9")]
        public void GivenANameString_WhenGeneratingSortIndex_ThenTheCorrectPropertiesAreAdded(string input, string expected)
        {
            var stringValue = new StringSearchValue(input);
            var values = new SortValue(stringValue, stringValue, new Uri("http://searchparameter"));

            var output = _generator.Generate(values);

            Assert.Equal(expected, output.Value<string>(SearchValueConstants.SortLowValueFieldName));
        }
    }
}
