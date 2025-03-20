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

        SqlServerBlobEnabled = 2,        // 0010

        SqlServerBlobDisabled = 4,       // 0100

        SqlServer = SqlServerBlobEnabled | SqlServerBlobDisabled,  // 0110

        All = CosmosDb | SqlServerBlobEnabled | SqlServerBlobDisabled, // 0111
    }
}
