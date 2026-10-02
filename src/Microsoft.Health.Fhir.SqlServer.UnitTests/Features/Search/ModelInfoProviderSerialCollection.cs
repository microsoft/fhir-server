// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class ModelInfoProviderSerialCollection
    {
        public const string Name = "SqlModelInfoProviderSerial";
    }
}
