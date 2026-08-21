// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// An XunitTestFramework implementation that allows parameterizing class fixtures with combinations of argument values.
    /// To use, decorate the test assembly with
    /// [assembly: TestFramework(typeof(CustomXunitTestFramework))]
    /// Assembly fixtures declared with <see cref="Xunit.AssemblyFixtureAttribute"/> are created before any tests run and
    /// disposed at the end of the run. That is xUnit v3 behaviour inherited from the base framework, not something this
    /// class adds: it previously needed a local implementation, which this assembly no longer carries.
    /// </summary>
    public sealed class CustomXunitTestFramework : XunitTestFramework
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CustomXunitTestFramework"/> class.
        /// </summary>
        public CustomXunitTestFramework()
            : base(configFileName: null)
        {
        }

        protected override ITestFrameworkDiscoverer CreateDiscoverer(Assembly assembly)
        {
            return new CustomXunitTestFrameworkDiscoverer(assembly);
        }

        protected override ITestFrameworkExecutor CreateExecutor(Assembly assembly)
        {
            return new CustomXunitTestFrameworkExecutor(assembly);
        }
    }
}
