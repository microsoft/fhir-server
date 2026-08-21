// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.BoundedParallelism
{
    /// <summary>
    /// The second of four classes, and so the second of four collections, in this scenario.
    /// </summary>
    public class BoundedParallelismTwoTests : BoundedParallelismTestBase
    {
        /// <summary>
        /// Runs the shared body that observes how many tests are running at once.
        /// </summary>
        /// <returns>A task that completes when the test has finished.</returns>
        [Fact]
        public Task Runs() => RunBody();
    }
}
