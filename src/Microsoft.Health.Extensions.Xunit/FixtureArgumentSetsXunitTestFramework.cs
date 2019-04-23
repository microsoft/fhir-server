// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// An XunitTestFramework implementation that allows parameterizing class fixtures.
    /// To use, decorate the test assembly with
    /// [assembly: TestFramework(typeName: "Microsoft.Health.Extensions.Xunit.FixtureArgumentSetsXunitTestFramework", assemblyName: "Microsoft.Health.Extensions.Xunit")]
    /// </summary>
    public class FixtureArgumentSetsXunitTestFramework : XunitTestFramework
    {
        public FixtureArgumentSetsXunitTestFramework(IMessageSink messageSink)
            : base(messageSink)
        {
        }

        protected override ITestFrameworkDiscoverer CreateDiscoverer(IAssemblyInfo assemblyInfo)
        {
            return new FixtureArgumentSetsXunitTestFrameworkDiscoverer(assemblyInfo, SourceInformationProvider, DiagnosticMessageSink);
        }

        protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName)
        {
            return new FixtureArgumentSetsXunitTestFrameworkExecutor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);
        }
    }
}
