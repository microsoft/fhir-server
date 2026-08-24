// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.AssemblyFixtureLifecycle
{
    /// <summary>
    /// An assembly fixture whose only effect is what its constructor does, which is how the real test
    /// assemblies use one: no test class asks for it, so a framework that created assembly fixtures
    /// only on demand would never construct it and the effect would simply not happen.
    /// </summary>
    public sealed class RecordingAssemblyFixture
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RecordingAssemblyFixture"/> class.
        /// </summary>
        public RecordingAssemblyFixture()
        {
            AssemblyFixtureProbe.Constructed = true;
        }
    }
}
