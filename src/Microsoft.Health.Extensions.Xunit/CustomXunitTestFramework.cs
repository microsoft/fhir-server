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
    /// Also allows an assembly to declare one or more <see cref="Xunit.AssemblyFixtureAttribute"/>, which are created before any tests
    /// are executed and disposed at the end of the test run.
    /// </summary>
    public sealed class CustomXunitTestFramework : XunitTestFramework
    {
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
