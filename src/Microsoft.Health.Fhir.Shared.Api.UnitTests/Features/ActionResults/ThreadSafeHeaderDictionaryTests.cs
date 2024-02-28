// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.ActionResults
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class ThreadSafeHeaderDictionaryTests
    {
        [Fact]
        public void GivenAGoneStatus_WhenReturningAResult_ThenTheContentShouldBeEmpty()
        {
            var headerDictionary = new HeaderDictionary();
            AggregateException aggregateException = Assert.Throws<AggregateException>(() => TestIHeaderDictionaryParallelism(headerDictionary));
            foreach (Exception innerException in aggregateException.InnerExceptions)
            {
                Assert.True(innerException is InvalidOperationException);
                Assert.Equal(
                    expected: "Operations that change non-concurrent collections must have exclusive access. A concurrent update was performed on this collection and corrupted its state. The collection's state is no longer correct.",
                    actual: innerException.Message);
            }

            var threadSafeHeaderDictionary = new ThreadSafeHeaderDictionary();
            TestIHeaderDictionaryParallelism(threadSafeHeaderDictionary);
            Assert.Single(threadSafeHeaderDictionary);
            Assert.NotNull(threadSafeHeaderDictionary.ContentLength);
        }

        private static void TestIHeaderDictionaryParallelism(IHeaderDictionary dictionary)
        {
            Parallel.For(0, 1000, i =>
            {
                dictionary.ContentLength = i;

                dictionary.Append("Test", DateTimeOffset.Now.ToString());

                System.Threading.Thread.Sleep(1);

                dictionary.Append("Test", DateTimeOffset.Now.ToString());

                dictionary.Remove("Test");

                dictionary.ContainsKey("Test");
            });
        }
    }
}
