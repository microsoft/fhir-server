// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Extensions;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Extensions
{
    public class DecimalExtensionTests
    {
        [Theory]
        [InlineData("100", ".5")]
        [InlineData("100.0", ".05")]
        [InlineData("100.00", ".005")]
        [InlineData("100.000", ".0005")]
        [InlineData("100.010", ".0005")]
        [InlineData("100.0000", ".00005")]
        [InlineData("100.00000", ".000005")]
        [InlineData("100.12345", ".000005")]
        [InlineData(".1234567890123456789012345678", "0")]
        public void GivenADecimal_WhenGetPrescisionModifierIsCalled_ThenCorrectDecimalIsReturned(string input, string expected)
        {
            var inputDecimal = decimal.Parse(input);
            var expectedDecimal = decimal.Parse(expected);
            Assert.Equal(expectedDecimal, inputDecimal.GetPrescisionModifier());
        }
    }
}
