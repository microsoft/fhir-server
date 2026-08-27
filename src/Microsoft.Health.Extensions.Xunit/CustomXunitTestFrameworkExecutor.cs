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
            // Deliberately not calling base.RunTestCases: it sets assertion-formatting environment variables before
            // invoking its fixed assembly runner. The custom runner is needed so the class runner can seed fixture
            // arguments; its context preserves xUnit's parallelism configuration and applies the conservative
            // collection limit to the custom dispatch path.
            var runner = new CustomXunitTestAssemblyRunner();
            await runner.Run(TestAssembly, testCases, executionMessageSink, executionOptions, cancellationToken);
        }
    }
}
