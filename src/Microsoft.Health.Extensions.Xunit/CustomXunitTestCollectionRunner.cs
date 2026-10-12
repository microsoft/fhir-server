// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// The collection runner that delegates each test class to <see cref="CustomXunitTestClassRunner"/>.
    /// </summary>
    internal sealed class CustomXunitTestCollectionRunner : XunitTestCollectionRunner
    {
        protected override ValueTask<RunSummary> RunTestClass(XunitTestCollectionRunnerContext context, IXunitTestClass testClass, IReadOnlyCollection<IXunitTestCase> testCases)
        {
            if (testClass == null)
            {
                return base.RunTestClass(context, testClass, testCases);
            }

            var orderer = context.TestCaseOrderer ?? DefaultTestCaseOrderer.Instance;
            var runner = new CustomXunitTestClassRunner();
            return runner.Run(testClass, testCases, context.ExplicitOption, context.MessageBus, orderer, context.Aggregator.Clone(), context.CancellationTokenSource, context.CollectionFixtureMappings);
        }
    }
}
