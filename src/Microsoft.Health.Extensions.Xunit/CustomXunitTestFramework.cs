// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// An <see cref="XunitTestFramework"/> implementation that allows parameterizing class fixtures with combinations of
    /// argument values (see <see cref="FixtureArgumentSetsAttribute"/>).
    /// To use, decorate the test assembly with <c>[assembly: TestFramework(typeof(CustomXunitTestFramework))]</c>.
    /// </summary>
    /// <remarks>
    /// Assembly-level fixtures are supported natively by xUnit v3 via <c>[assembly: AssemblyFixture(...)]</c>, so this
    /// framework no longer carries a custom assembly-fixture implementation.
    /// </remarks>
    public sealed class CustomXunitTestFramework : XunitTestFramework
    {
        /// <inheritdoc/>
        protected override ITestFrameworkDiscoverer CreateDiscoverer(Assembly assembly) => new CustomXunitTestFrameworkDiscoverer(assembly);

        /// <inheritdoc/>
        protected override ITestFrameworkExecutor CreateExecutor(Assembly assembly) => new CustomXunitTestFrameworkExecutor(assembly);
    }
}
