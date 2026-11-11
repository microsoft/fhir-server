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
        protected override async ValueTask<RunSummary> RunTestClass(XunitTestCollectionRunnerContext context, IXunitTestClass testClass, IReadOnlyCollection<IXunitTestCase> testCases)
        {
            if (testClass == null)
            {
                return await base.RunTestClass(context, testClass, testCases);
            }

            var runner = new CustomXunitTestClassRunner();
            if (testClass is not FixtureArgumentSetTestClass variantClass)
            {
                return await runner.Run(testClass, testCases, context.ExplicitOption, context.MessageBus, context.Aggregator.Clone(), context.CancellationTokenSource, context.ParallelMode, context.Scheduler, context.CollectionFixtureMappings);
            }

            await using var arguments = CustomXunitTestClassRunner.CreateFixtureArguments(variantClass, context.CollectionFixtureMappings);
            return await runner.Run(testClass, testCases, context.ExplicitOption, context.MessageBus, context.Aggregator.Clone(), context.CancellationTokenSource, context.ParallelMode, context.Scheduler, arguments);
        }
    }
}
