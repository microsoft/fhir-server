// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Tests.Common.FixtureParameters
{
    [Flags]
    public enum DataStore
    {
        CosmosDb = 1,                    // 0001

        SqlServerBlobDisabled = 2,        // 0010

        SqlServerBlobEnabled = 4,       // 0100

        SqlServer = SqlServerBlobDisabled | SqlServerBlobEnabled,  // 0110

        All = CosmosDb | SqlServerBlobDisabled | SqlServerBlobEnabled, // 0111
    }
}
