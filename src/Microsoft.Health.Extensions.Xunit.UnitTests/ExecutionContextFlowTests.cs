// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Records what the custom executor does with the execution context a class fixture is built in.
    /// </summary>
    public class ExecutionContextFlowTests
    {
        private const string Prefix = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ExecutionContextFlow.AsyncLocalFlowTests.";

        /// <summary>
        /// A value written to an <see cref="System.Threading.AsyncLocal{T}"/> by a class fixture
        /// constructor is not readable from the test methods of that class. xunit.v2 preserved that
        /// flow and xunit.v3 cannot, because it builds fixtures inside an async method whose state
        /// machine restores the caller's execution context on return. The scenario asserts the value
        /// is absent, so this run turning red means the flow came back and the notes describing its
        /// absence need revisiting.
        /// </summary>
        [Fact]
        public void GivenAFixtureThatWritesToTheExecutionContext_WhenItsTestsRun_ThenTheWriteIsNotVisibleToThem()
        {
            TestAssetRun run = TestAssetRunner.Run("ExecutionContextFlow");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [Prefix + "DoesNotSeeTheValueWrittenByTheFixtureConstructor"] = "Passed",
                });
        }
    }
}
