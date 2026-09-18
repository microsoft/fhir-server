// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Xunit;

[assembly: TestFramework(typeName: CustomXunitTestFramework.TypeName, assemblyName: CustomXunitTestFramework.AssemblyName)]
[assembly: AssemblyFixture(typeof(SetModelInfoProviderAssemblyFixture))]
[assembly: AssemblyFixture(typeof(TestFhirServerFactory))]

// Allows this assembly to be split across several test processes. Sharding stays off unless the
// MicrosoftHealthTestShardIndex and MicrosoftHealthTestShardCount environment variables are set, and each shard
// process must target its own FHIR service and database. See docs/TestSharding.md.
[assembly: EnableTestSharding]
