// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Reflection;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Formatters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class NonFhirResourceXmlOutputFormatterTests
    {
        [Theory]
        [InlineData(typeof(Patient), false)]
        [InlineData(typeof(NonFhirResourceXmlOutputFormatter), true)]
        public void GivenType_WhenCheckingCanWriteType_ThenFormatterShouldReturnCorrectValue(
            Type type,
            bool expected)
        {
            var formatter = new NonFhirResourceXmlOutputFormatter();
            var methodInfo = typeof(NonFhirResourceXmlOutputFormatter).GetMethod(
                "CanWriteType",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var actual = methodInfo.Invoke(
                formatter,
                new object[] { type });
            Assert.Equal(expected, actual);
        }
    }
}
