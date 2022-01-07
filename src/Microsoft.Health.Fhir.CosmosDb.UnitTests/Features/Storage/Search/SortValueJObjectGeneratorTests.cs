// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Search;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage.Search
{
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

            Console.WriteLine("Input {0}, Output {1}", input, output.Value<string>(SearchValueConstants.SortLowValueFieldName));

            Console.Write("Characters in input = ");
            foreach (short x in input)
            {
                Console.Write("{0:X4} ", x);
            }

            Console.WriteLine();

            Console.Write("Characters in output = ");
            foreach (short x in output.Value<string>(SearchValueConstants.SortLowValueFieldName))
            {
                Console.Write("{0:X4} ", x);
            }

            Console.WriteLine();

            Assert.Equal(expected, output.Value<string>(SearchValueConstants.SortLowValueFieldName));
        }

        [Fact]
        public void GivenAString_WhenNormalized_ExpectedValuesAreReturned()
        {
            var testString = "Müller";
            var normalizedString = testString.Normalize(System.Text.NormalizationForm.FormKD);

            Console.Write("Characters in normalized string = ");
            foreach (short x in normalizedString)
            {
                Console.Write("{0:X4} ", x);
            }

            Console.WriteLine();

            var charArray = normalizedString.ToCharArray();
            Assert.Equal((char)117, charArray[1]); // Lowercase u
            Assert.Equal((char)776, charArray[2]); // Combining Diaeresis
        }
    }
}
