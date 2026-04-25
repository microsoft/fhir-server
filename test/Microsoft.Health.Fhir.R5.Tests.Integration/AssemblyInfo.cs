// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

[assembly: TestFramework(typeof(CustomXunitTestFramework))]
[assembly: Xunit.AssemblyFixtureAttribute(typeof(SetModelInfoProviderAssemblyFixture))]
