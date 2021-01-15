// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient.Server;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry
{
    public class SearchParameterHashCollection : List<KeyValuePair<short, string>>, IEnumerable<SqlDataRecord>
    {
        IEnumerator<SqlDataRecord> IEnumerable<SqlDataRecord>.GetEnumerator()
        {
            var sqlRow = new SqlDataRecord(
                new SqlMetaData("Id", SqlDbType.SmallInt),
                new SqlMetaData("Hash", SqlDbType.VarChar, 128));

            foreach (KeyValuePair<short, string> pair in this)
            {
                sqlRow.SetInt16(0, pair.Key);
                sqlRow.SetString(1, pair.Value);

                yield return sqlRow;
            }
        }
    }
}
