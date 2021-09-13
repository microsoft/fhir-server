// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations.Everything;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Everything
{
    public class EverythingOperationContinuationTokenTests
    {
        [Fact]
        public void GivenAString_WhenFromString_ThenCorrectEverythingOperationContinuationTokenShouldBeReturned()
        {
            Assert.Null(EverythingOperationContinuationToken.FromString(null));
            Assert.Null(EverythingOperationContinuationToken.FromString(string.Empty));
            Assert.Null(EverythingOperationContinuationToken.FromString(" "));
            Assert.Null(EverythingOperationContinuationToken.FromString("abc"));

            var token = EverythingOperationContinuationToken.FromString("{\"a\":\"b\"}");
            Assert.Equal(0, token.Phase);
            Assert.Null(token.InternalContinuationToken);

            token = EverythingOperationContinuationToken.FromString("{\"Phase\":3}");
            Assert.Equal(3, token.Phase);
            Assert.Null(token.InternalContinuationToken);

            token = EverythingOperationContinuationToken.FromString("{\"Phase\":1,\"InternalContinuationToken\":null}");
            Assert.Equal(1, token.Phase);
            Assert.Null(token.InternalContinuationToken);

            token = EverythingOperationContinuationToken.FromString("{\"Phase\":2,\"InternalContinuationToken\":\"abc\"}");
            Assert.Equal(2, token.Phase);
            Assert.Equal("abc", token.InternalContinuationToken);
        }

        [Theory]
        [InlineData(0, null)]
        [InlineData(1, null)]
        [InlineData(2, "abc")]
        [InlineData(3, "abc")]
        public void GivenEverythingOperationContinuationToken_WhenToString_ThenCorrectStringShouldBeReturned(int phase, string internalContinuationToken)
        {
            var token = new EverythingOperationContinuationToken(phase, internalContinuationToken);

            // The internal continuation token value will be padded with quotes if it is not null
            internalContinuationToken = string.IsNullOrEmpty(internalContinuationToken) ? "null" : "\"" + internalContinuationToken + "\"";

            Assert.Equal($"{{\"SeeAlsoLinks\":[],\"Phase\":{phase},\"InternalContinuationToken\":{internalContinuationToken},\"CurrentSeeAlsoLinkIndex\":-1}}", token.ToString());
        }

        [Fact]
        public void GivenEverythingOperationContinuationTokenWithSeeAlsoLinks_WhenToString_ThenCorrectStringShouldBeReturned()
        {
            var token = new EverythingOperationContinuationToken(0, null);
            token.SeeAlsoLinks.Add("link1");
            token.SeeAlsoLinks.Add("link2");

            token.ProcessNextSeeAlsoLink();
            Assert.Equal($"{{\"SeeAlsoLinks\":[\"link1\",\"link2\"],\"Phase\":0,\"InternalContinuationToken\":null,\"CurrentSeeAlsoLinkIndex\":0}}", token.ToString());

            token.ProcessNextSeeAlsoLink();
            Assert.Equal($"{{\"SeeAlsoLinks\":[\"link1\",\"link2\"],\"Phase\":0,\"InternalContinuationToken\":null,\"CurrentSeeAlsoLinkIndex\":1}}", token.ToString());
        }
    }
}
