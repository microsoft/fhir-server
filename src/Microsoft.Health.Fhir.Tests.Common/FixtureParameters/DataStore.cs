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
        CosmosDb = 1,

        SqlServer = 2,

        All = CosmosDb | SqlServer,
    }
}
