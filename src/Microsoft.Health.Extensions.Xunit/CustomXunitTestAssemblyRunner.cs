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
    internal sealed class CustomXunitTestAssemblyRunner : XunitTestAssemblyRunnerBase<CustomXunitTestAssemblyRunnerContext, IXunitTestAssembly, IXunitTestCollection, IXunitTestCase>
    {
        public async ValueTask<RunSummary> Run(IXunitTestAssembly testAssembly, IReadOnlyCollection<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions, CancellationToken cancellationToken)
        {
            await using var context = new CustomXunitTestAssemblyRunnerContext(testAssembly, testCases, executionMessageSink, executionOptions, cancellationToken);
            await context.InitializeAsync();
            return await Run(context);
        }

        protected override ValueTask<RunSummary> RunTestCollection(CustomXunitTestAssemblyRunnerContext context, IXunitTestCollection testCollection, IReadOnlyCollection<IXunitTestCase> testCases)
        {
            var orderer = context.AssemblyTestCaseOrderer ?? DefaultTestCaseOrderer.Instance;
            return context.RunCollection(testCollection, testCases, orderer);
        }
    }
}
