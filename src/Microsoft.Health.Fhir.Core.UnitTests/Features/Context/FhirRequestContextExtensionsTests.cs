// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Context
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class FhirRequestContextExtensionsTests
    {
        private const string Key = "EnableFhirDateContainment";

        private static IFhirRequestContext CreateContext()
        {
            return new FhirRequestContext(
                method: "GET",
                uriString: "https://localhost/",
                baseUriString: "https://localhost/",
                correlationId: "correlation-id",
                requestHeaders: new Dictionary<string, StringValues>(),
                responseHeaders: new Dictionary<string, StringValues>());
        }

        [Fact]
        public void GivenOverridesAreSet_WhenReadByKey_ThenTheValueIsReturned()
        {
            IFhirRequestContext context = CreateContext();
            context.SetRequestConfigurationOverrides(new Dictionary<string, string> { [Key] = "true" });

            Assert.True(context.TryGetRequestConfigurationOverride(Key, out string value));
            Assert.Equal("true", value);
        }

        [Fact]
        public void GivenOverridesAreSet_WhenReadByKeyWithDifferentCasing_ThenTheValueIsReturned()
        {
            IFhirRequestContext context = CreateContext();
            context.SetRequestConfigurationOverrides(new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) { [Key] = "true" });

            Assert.True(context.TryGetRequestConfigurationOverride("enablefhirdatecontainment", out string value));
            Assert.Equal("true", value);
        }

        [Fact]
        public void GivenNoOverrides_WhenRead_ThenNothingIsReturned()
        {
            IFhirRequestContext context = CreateContext();

            Assert.False(context.TryGetRequestConfigurationOverride(Key, out _));
            Assert.Null(context.GetBooleanConfigurationOverride(Key));
        }

        [Fact]
        public void GivenAnEmptyOverrideSet_WhenSet_ThenNothingIsStored()
        {
            IFhirRequestContext context = CreateContext();
            context.SetRequestConfigurationOverrides(new Dictionary<string, string>());

            Assert.False(context.TryGetRequestConfigurationOverride(Key, out _));
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("false", false)]
        [InlineData("True", true)]
        public void GivenAParseableBooleanOverride_WhenReadAsBoolean_ThenItIsParsed(string raw, bool expected)
        {
            IFhirRequestContext context = CreateContext();
            context.SetRequestConfigurationOverrides(new Dictionary<string, string> { [Key] = raw });

            Assert.Equal(expected, context.GetBooleanConfigurationOverride(Key));
        }

        [Fact]
        public void GivenAnUnparseableBooleanOverride_WhenReadAsBoolean_ThenNullIsReturned()
        {
            IFhirRequestContext context = CreateContext();
            context.SetRequestConfigurationOverrides(new Dictionary<string, string> { [Key] = "not-a-bool" });

            Assert.Null(context.GetBooleanConfigurationOverride(Key));
        }

        [Fact]
        public void GivenANullContext_WhenRead_ThenNullIsReturnedSafely()
        {
            IFhirRequestContext context = null;

            Assert.False(context.TryGetRequestConfigurationOverride(Key, out _));
            Assert.Null(context.GetBooleanConfigurationOverride(Key));
        }
    }
}
