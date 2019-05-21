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
    /// An XunitTestFramework implementation that allows parameterizing class fixtures with combinations of argument values.
    /// To use, decorate the test assembly with
    /// [assembly: TestFramework(typeName: CustomXunitTestFramework.TypeName, assemblyName: CustomXunitTestFramework.AssemblyName)]
    /// Also allows an assembly to declare one or more <see cref="AssemblyFixtureAttribute"/>, which are created before any tests
    /// are executed and disposed at the end of the test run.
    /// </summary>
    public class CustomXunitTestFramework : XunitTestFramework
    {
        /// <summary>
        /// This type's assembly name.
        /// </summary>
        public const string AssemblyName = nameof(Microsoft) + "." +
                                           nameof(Microsoft.Health) + "." +
                                           nameof(Microsoft.Health.Extensions) + "." +
                                           nameof(Microsoft.Health.Extensions.Xunit);

        /// <summary>
        /// The full name of this type. Intended to be used as an attribute argument.
        /// </summary>
        public const string TypeName = AssemblyName + "." + nameof(CustomXunitTestFramework);

        public CustomXunitTestFramework(IMessageSink messageSink)
            : base(messageSink)
        {
        }

        protected override ITestFrameworkDiscoverer CreateDiscoverer(IAssemblyInfo assemblyInfo)
        {
            return new CustomXunitTestFrameworkDiscoverer(assemblyInfo, SourceInformationProvider, DiagnosticMessageSink);
        }

        protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName)
        {
            return new CustomXunitTestFrameworkExecutor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);
        }
    }
}
