// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// The custom <see cref="XunitTestFrameworkExecutor"/> that seeds each fixtured test class's chosen
    /// <c>(DataStore, Format)</c> values into the fixture cache and the class constructor before the class runs.
    /// </summary>
    internal sealed class CustomXunitTestFrameworkExecutor : XunitTestFrameworkExecutor
    {
        public CustomXunitTestFrameworkExecutor(Assembly assembly)
            : base(new XunitTestAssembly(assembly, null, assembly.GetName().Version, UniqueIDGenerator.ForAssembly(assembly.Location, null)))
        {
        }

        /// <inheritdoc/>
        public override async ValueTask RunTestCases(IReadOnlyCollection<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions, CancellationToken cancellationToken)
        {
            // Deliberately not calling base.RunTestCases: the base additionally copies assertion-formatting options into
            // environment variables and installs a parallelism semaphore. Both are cosmetic here - they affect
            // assertion-message truncation and MaxParallelThreads, never which tests run or their pass/fail result - and
            // substituting the assembly runner is what lets the class runner seed fixture arguments.
            var runner = new CustomXunitTestAssemblyRunner();
            await runner.Run(TestAssembly, testCases, executionMessageSink, executionOptions, cancellationToken);
        }
    }
}
