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

        [Fact]
        public void GivenEverythingOperationContinuationToken_WhenToString_ThenCorrectStringShouldBeReturned()
        {
            Assert.Equal("{\"Phase\":1,\"InternalContinuationToken\":null}", EverythingOperationContinuationToken.ToString(1, null));
            Assert.Equal("{\"Phase\":2,\"InternalContinuationToken\":\"abc\"}", EverythingOperationContinuationToken.ToString(2, "abc"));
        }
    }
}
