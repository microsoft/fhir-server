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
    /// The assembly runner that delegates each collection to <see cref="CustomXunitTestCollectionRunner"/>.
    /// </summary>
    internal sealed class CustomXunitTestAssemblyRunner : XunitTestAssemblyRunnerBase<XunitTestAssemblyRunnerContext, IXunitTestAssembly, IXunitTestCollection, IXunitTestCase>
    {
        public async ValueTask<RunSummary> Run(IXunitTestAssembly testAssembly, IReadOnlyCollection<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions, CancellationToken cancellationToken)
        {
            await using var context = new XunitTestAssemblyRunnerContext(testAssembly, testCases, executionMessageSink, executionOptions, cancellationToken);
            await context.InitializeAsync();
            return await Run(context);
        }

        protected override ValueTask<RunSummary> RunTestCollection(XunitTestAssemblyRunnerContext context, IXunitTestCollection testCollection, IReadOnlyCollection<IXunitTestCase> testCases)
        {
            var orderer = context.AssemblyTestCaseOrderer ?? DefaultTestCaseOrderer.Instance;
            var runner = new CustomXunitTestCollectionRunner();

            // Aggregator is a struct wrapping a mutable list; clone so collections don't share one.
            return runner.Run(testCollection, testCases, context.ExplicitOption, context.MessageBus, orderer, context.Aggregator.Clone(), context.CancellationTokenSource, context.AssemblyFixtureMappings);
        }
    }
}
