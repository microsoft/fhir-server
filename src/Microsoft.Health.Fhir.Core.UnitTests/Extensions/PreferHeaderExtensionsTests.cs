// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Extensions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.ConditionalOperations)]
    public class PreferHeaderExtensionsTests
    {
        [Theory]
        [InlineData("handling=strict", SearchParameterHandling.Strict, false)]
        [InlineData("handling=lenient", SearchParameterHandling.Lenient, false)]
        [InlineData("handling=Lenient", SearchParameterHandling.Lenient, false)]
        [InlineData("handling=unknown", null, true)]
        [InlineData("handlings=strict", null, false)]
        [InlineData("abcdefg", null, false)]
        public void GivenARequestContextAccessor_WhenCheckingHandlingrHeader_ThenSearchParameterHandlingShouldBeReadSuccessfully(
            string preferHeaderValue,
            SearchParameterHandling? expected,
            bool throwException)
        {
            var headers = new Dictionary<string, StringValues>();
            if (!string.IsNullOrEmpty(preferHeaderValue))
            {
                headers.Add(KnownHeaders.Prefer, preferHeaderValue);
            }

            var fhirRequestContext = Substitute.For<IFhirRequestContext>();
            fhirRequestContext.RequestHeaders.Returns(headers);

            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            requestContextAccessor.RequestContext.Returns(fhirRequestContext);

            try
            {
                var actual = requestContextAccessor.GetHandlingHeader();
                Assert.Equal(actual, expected);
                Assert.False(throwException);
            }
            catch (BadRequestException)
            {
                Assert.True(throwException);
            }
        }

        [Theory]
        [InlineData("return=minimal", ReturnPreference.Minimal, false)]
        [InlineData("return=representation", ReturnPreference.Representation, false)]
        [InlineData("return=OperationOutcome", ReturnPreference.OperationOutcome, false)]
        [InlineData("return=operatioNoutcomE", ReturnPreference.OperationOutcome, false)]
        [InlineData("return=unknown", null, true)]
        [InlineData("returns=minimal", null, false)]
        [InlineData("abcdefg", null, false)]
        public void GivenARequestContextAccessor_WhenCheckingPreferHeader_ThenReturnPreferenceShouldBeReadSuccessfully(
            string preferHeaderValue,
            ReturnPreference? expected,
            bool throwException)
        {
            var headers = new Dictionary<string, StringValues>();
            if (!string.IsNullOrEmpty(preferHeaderValue))
            {
                headers.Add(KnownHeaders.Prefer, preferHeaderValue);
            }

            var fhirRequestContext = Substitute.For<IFhirRequestContext>();
            fhirRequestContext.RequestHeaders.Returns(headers);

            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            requestContextAccessor.RequestContext.Returns(fhirRequestContext);

            try
            {
                var actual = requestContextAccessor.GetReturnPreferenceValue();
                Assert.Equal(actual, expected);
                Assert.False(throwException);
            }
            catch (BadRequestException)
            {
                Assert.True(throwException);
            }
        }
    }
}
