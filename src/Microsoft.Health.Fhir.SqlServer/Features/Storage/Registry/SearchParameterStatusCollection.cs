// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry
{
    public class SearchParameterStatusCollection : List<ResourceSearchParameterStatus>, IEnumerable<SqlDataRecord>
    {
        private readonly bool _includeRowVersion;

        public SearchParameterStatusCollection(bool includeRowVersion = false)
        {
            _includeRowVersion = includeRowVersion;
        }

        IEnumerator<SqlDataRecord> IEnumerable<SqlDataRecord>.GetEnumerator()
        {
            SqlDataRecord sqlRow;

            if (_includeRowVersion)
            {
                // 4-column version for SearchParamTableType_3
                sqlRow = new SqlDataRecord(
                    new SqlMetaData("Uri", SqlDbType.VarChar, 128),
                    new SqlMetaData("Status", SqlDbType.VarChar, 20),
                    new SqlMetaData("IsPartiallySupported", SqlDbType.Bit),
                    new SqlMetaData("RowVersion", SqlDbType.VarBinary, 8));
            }
            else
            {
                // 3-column version for SearchParamTableType_2
                sqlRow = new SqlDataRecord(
                    new SqlMetaData("Uri", SqlDbType.VarChar, 128),
                    new SqlMetaData("Status", SqlDbType.VarChar, 20),
                    new SqlMetaData("IsPartiallySupported", SqlDbType.Bit));
            }

            foreach (ResourceSearchParameterStatus status in this)
            {
                sqlRow.SetString(0, status.Uri.OriginalString);
                sqlRow.SetString(1, status.Status.ToString());
                sqlRow.SetSqlBoolean(2, status.IsPartiallySupported);

                if (_includeRowVersion)
                {
                    if (status.RowVersion != null)
                    {
                        sqlRow.SetSqlBinary(3, status.RowVersion);
                    }
                    else
                    {
                        sqlRow.SetDBNull(3);
                    }
                }

                yield return sqlRow;
            }
        }
    }
}
