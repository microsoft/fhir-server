// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchValues
{
    public class ReferenceSearchValueTests
    {
        private const string ParamNameReference = "reference";
        private const string ParamNameS = "s";

        [Fact]
        public void GivenANullReference_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameReference, () => new ReferenceSearchValue(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidReference_WhenInitializing_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamNameReference, () => new ReferenceSearchValue(s));
        }

        [Fact]
        public void GivenANullString_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameS, () => ReferenceSearchValue.Parse(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("    ")]
        public void GivenAnInvalidString_WhenParsing_ThenExceptionShouldBeThrown(string s)
        {
            Assert.Throws<ArgumentException>(ParamNameS, () => ReferenceSearchValue.Parse(s));
        }

        [Theory]
        [InlineData("http://localhost/", ResourceType.Patient, "123", "http://localhost/Patient/123")]
        [InlineData(null, ResourceType.Observation, "xyz", "Observation/xyz")]
        [InlineData(null, null, "abc", "abc")]
        public void GivenASearchValue_WhenToStringIsCalled_ThenCorrectStringShouldBeReturned(
            string uriString,
            ResourceType? resourceType,
            string resourceId,
            string expected)
        {
            Uri uri = null;

            if (uriString != null)
            {
                uri = new Uri(uriString);
            }

            var value = new ReferenceSearchValue(ReferenceKind.InternalOrExternal, uri, resourceType, resourceId);

            Assert.Equal(expected, value.ToString());
        }
    }
}
