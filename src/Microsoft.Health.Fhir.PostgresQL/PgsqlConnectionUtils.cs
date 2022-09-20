// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Npgsql;
using static Microsoft.Health.Fhir.PostgresQL.TypeConvert;

namespace Microsoft.Health.Fhir.PostgresQL
{
    internal static class PgsqlConnectionUtils
    {
        public static async Task<NpgsqlConnection> CreateAndOpenConnectionAsync()
        {
            string connectionSting = "";
            new NpgsqlConnectionStringBuilder(connectionSting) { Enlist = true }.ToString();
            NpgsqlConnection connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionSting) { Enlist = true }.ToString());
            await connection.OpenAsync(CancellationToken.None);

            connection.TypeMapper.MapComposite<BulkResourceWriteClaimTableTypeV1Row>("bulkresourcewriteclaimtabletype_1");
            connection.TypeMapper.MapComposite<BulkTokenTextTableTypeV1Row>("bulktokentexttabletype_2");

            return connection;
        }
    }
}
