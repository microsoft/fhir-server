// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Formatters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class JsonArrayPoolTests
    {
        private const int OneHunderMegabytes = 100000000;   // 100MB.
        private const int ThirtyMegabytes = 30000000;       // 30MB.

        [Fact]
        public void GivenAnAttemptToAllocateAJsonArrayPool_WhenAlocatingAHugeAmountOfData_ThenThrownAnError()
        {
            JsonArrayPool pool = new JsonArrayPool(ArrayPool<char>.Shared);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                pool.Rent(OneHunderMegabytes);
            });
        }

        [Fact]
        public void GivenAnAttemptToAllocateAJsonArrayPool_WhenAlocatingAnAcceptableAmountOfData_ThenReserveMemory()
        {
            JsonArrayPool pool = new JsonArrayPool(ArrayPool<char>.Shared);
            pool.Rent(ThirtyMegabytes);
        }
    }
}
