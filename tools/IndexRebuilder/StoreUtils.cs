// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data.SqlClient;

namespace Microsoft.Health.Fhir.IndexRebuilder
{
    internal class StoreUtils
    {
        internal static string ShowConnectionString(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            return $"server={builder.DataSource};database={builder.InitialCatalog}";
        }
    }
}
