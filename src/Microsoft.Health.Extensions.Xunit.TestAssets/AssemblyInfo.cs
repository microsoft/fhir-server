// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.AssemblyFixtureLifecycle;
using Xunit;

// Exercise the same custom framework the real test projects use, so these assets
// cover the discoverer and executor shims and not just the retry test case.
[assembly: TestFramework(typeof(CustomXunitTestFramework))]

// Nine test assemblies declare an assembly fixture that no test class asks for and rely on its
// constructor alone to install the FHIR model info provider. Nothing they contain asserts that the
// fixture ran, so if the framework stopped creating unrequested fixtures those assemblies would fail
// in whatever way a missing provider happens to look like. Declaring one here the same way makes
// that assumption something a test can see.
[assembly: AssemblyFixture(typeof(RecordingAssemblyFixture))]
