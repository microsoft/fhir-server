// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations.Everything;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Everything
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class EverythingOperationContinuationTokenTests
    {
        [Fact]
        public void GivenAString_WhenFromString_ThenCorrectEverythingOperationContinuationTokenShouldBeReturned()
        {
            Assert.Null(EverythingOperationContinuationToken.FromJson(null));
            Assert.Null(EverythingOperationContinuationToken.FromJson(string.Empty));
            Assert.Null(EverythingOperationContinuationToken.FromJson(" "));
            Assert.Null(EverythingOperationContinuationToken.FromJson("abc"));

            var token = EverythingOperationContinuationToken.FromJson("{\"a\":\"b\"}");
            Assert.Equal(0, token.Phase);
            Assert.Null(token.InternalContinuationToken);

            token = EverythingOperationContinuationToken.FromJson("{\"Phase\":3}");
            Assert.Equal(3, token.Phase);
            Assert.Null(token.InternalContinuationToken);

            token = EverythingOperationContinuationToken.FromJson("{\"Phase\":1,\"InternalContinuationToken\":null}");
            Assert.Equal(1, token.Phase);
            Assert.Null(token.InternalContinuationToken);

            token = EverythingOperationContinuationToken.FromJson("{\"Phase\":2,\"InternalContinuationToken\":\"abc\"}");
            Assert.Equal(2, token.Phase);
            Assert.Equal("abc", token.InternalContinuationToken);
        }

        [Theory]
        [InlineData(0, null, null)]
        [InlineData(1, null, "test")]
        [InlineData(2, "abc", null)]
        [InlineData(3, "abc", "1234567890")]
        public void GivenEverythingOperationContinuationToken_WhenToString_ThenCorrectStringShouldBeReturned(int phase, string internalContinuationToken, string currentSeeAlsoLinkId)
        {
            var token = new EverythingOperationContinuationToken
            {
                Phase = phase,
                InternalContinuationToken = internalContinuationToken,
                CurrentSeeAlsoLinkId = currentSeeAlsoLinkId,
            };

            // Values will be padded with quotes if they are not null
            internalContinuationToken = string.IsNullOrEmpty(internalContinuationToken) ? "null" : "\"" + internalContinuationToken + "\"";
            currentSeeAlsoLinkId = string.IsNullOrEmpty(currentSeeAlsoLinkId) ? "null" : "\"" + currentSeeAlsoLinkId + "\"";

            Assert.Equal($"{{\"Phase\":{phase},\"InternalContinuationToken\":{internalContinuationToken},\"CurrentSeeAlsoLinkId\":{currentSeeAlsoLinkId},\"ParentPatientVersionId\":null}}", token.ToJson());
        }
    }
}
