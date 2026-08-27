// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// The assembly-runner context used by <see cref="CustomXunitTestAssemblyRunner"/>.
    /// </summary>
    /// <remarks>
    /// It exists only to reach protected members (<c>MessageBus</c>, <c>ExplicitOption</c>, <c>Aggregator</c>,
    /// <c>CancellationTokenSource</c>, <c>AssemblyFixtureMappings</c>) from the runner so it can launch a custom
    /// collection runner.
    /// </remarks>
    internal sealed class CustomXunitTestAssemblyRunnerContext : XunitTestAssemblyRunnerContext
    {
        public CustomXunitTestAssemblyRunnerContext(IXunitTestAssembly testAssembly, IReadOnlyCollection<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions, CancellationToken cancellationToken)
            : base(testAssembly, testCases, executionMessageSink, executionOptions, cancellationToken)
        {
        }

        public ValueTask<RunSummary> RunCollection(IXunitTestCollection testCollection, IReadOnlyCollection<IXunitTestCase> testCases, ITestCaseOrderer orderer)
        {
            var runner = new CustomXunitTestCollectionRunner();

            // Aggregator is a struct wrapping a mutable list; clone so collections don't share one.
            return runner.Run(testCollection, testCases, ExplicitOption, MessageBus, orderer, Aggregator.Clone(), CancellationTokenSource, AssemblyFixtureMappings);
        }
    }
}
