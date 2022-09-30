// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ContinuationTokenConverterTests
    {
        [Fact]
        public void GivenAString_WhenEcodingAndDecoding_ThenOriginalStringIsPreserved()
        {
            var data = Guid.NewGuid().ToString();

            var encoded = ContinuationTokenConverter.Encode(data);
            var decoded = ContinuationTokenConverter.Decode(encoded);

            Assert.Equal(data, decoded);
        }

        [Fact]
        public void GivenAnOldStringInBase64_WhenDecoding_ThenOriginalStringIsPreserved()
        {
            var data = Guid.NewGuid().ToString();

            var encodedPrevious = Convert.ToBase64String(Encoding.UTF8.GetBytes(data));

            var decoded = ContinuationTokenConverter.Decode(encodedPrevious);

            Assert.Equal(data, decoded);
        }

        [Fact]
        public void GivenAnInvalidString_WhenDecoding_ThenAnErrorIsThrown()
        {
            var data = Guid.NewGuid().ToString();

            var encodedPrevious = Convert.ToBase64String(Encoding.UTF8.GetBytes(data)).Insert(5, "aaaafffff");

            Assert.Throws<BadRequestException>(() => ContinuationTokenConverter.Decode(encodedPrevious));
        }

        [Fact]
        public void GivenShortBase64WhenDecoding_ThenCorrectValueIsReturned()
        {
            var data = "YWJj";

            var decoded = ContinuationTokenConverter.Decode(data);

            Assert.Equal("abc", decoded);
        }
    }
}
