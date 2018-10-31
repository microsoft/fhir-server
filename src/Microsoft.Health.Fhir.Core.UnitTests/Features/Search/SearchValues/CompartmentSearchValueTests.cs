// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchValues
{
    public class CompartmentSearchValueTests
    {
        private const string ParamNameS = "resourceIds";

        [Fact]
        public void GivenNullResourceIds_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            Assert.Throws<ArgumentNullException>(ParamNameS, () => new CompartmentSearchValue(CompartmentType.Patient, null));
        }

        [Fact]
        public void GivenInvalidResourceIds_WhenInitializing_ThenExceptionShouldBeThrown()
        {
            var list = new List<string>();
            list.Add("    ");
            list.Add("example");
            Assert.Throws<ArgumentException>(ParamNameS, () => new CompartmentSearchValue(CompartmentType.Patient, list));
        }

        [Fact]
        public void GivenValidResourceIds_WhenInitialized_ThenCorrectResourceIdsShouldBeReturned()
        {
            var list = new List<string>();
            list.Add("example1");
            list.Add("example2");
            list.Add("example3");

            var compartmentSearchValue = new CompartmentSearchValue(CompartmentType.Patient, list);

            Assert.Equal(3, compartmentSearchValue.ResourceIds.Count);
            Assert.Contains("example1", compartmentSearchValue.ResourceIds);
            Assert.Contains("example2", compartmentSearchValue.ResourceIds);
            Assert.Contains("example3", compartmentSearchValue.ResourceIds);
        }

        [Fact]
        public void GivenACompartmentSearchValue_WhenIsValidCompositeComponentIsCalled_ThenTrueShouldBeReturned()
        {
            var list = new List<string>();
            list.Add("example1");
            list.Add("example2");
            list.Add("example3");
            var compartmentSearchValue = new CompartmentSearchValue(CompartmentType.Patient, list);
            Assert.True(compartmentSearchValue.IsValidAsCompositeComponent);
        }
    }
}
