// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Defaults for using a local database.
    /// </summary>
    public static class LocalDatabase
    {
        public const string DefaultConnectionString = "server=(local);Initial Catalog=FHIR;Integrated Security=true";
    }
}
