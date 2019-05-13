// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests
{
    public class EnumerableExtensionsTests
    {
        [InlineData(null)]
        [InlineData("")]
        [Theory]
        public void GivenAnNullOrEmptyEnumerable_WhenCallingNullIfEmpty_ReturnsNull(string commaSeparatedInput)
        {
            string[] input = commaSeparatedInput?.Split(',', StringSplitOptions.RemoveEmptyEntries);
            Assert.Null(input.NullIfEmpty());
        }

        [InlineData("1")]
        [InlineData("1,2")]
        [InlineData("1,2,3")]
        [Theory]
        public void GivenANonEmptyEnumerable_WhenCallingNullIfEmpty_ReturnsTheExpectedSequence(string commaSeparatedInput)
        {
            string[] inputSequence = commaSeparatedInput?.Split(',', StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<string> wrappedSequence = inputSequence.NullIfEmpty();
            Assert.Equal(commaSeparatedInput, string.Join(",", wrappedSequence));
            Assert.Equal(commaSeparatedInput, string.Join(",", wrappedSequence));
        }
    }
}
