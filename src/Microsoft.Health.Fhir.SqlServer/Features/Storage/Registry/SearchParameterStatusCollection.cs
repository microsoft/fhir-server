// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry
{
    public class SearchParameterStatusCollection : List<ResourceSearchParameterStatus>, IEnumerable<SqlDataRecord>
    {
        private readonly bool _includeLastUpdated;

        public SearchParameterStatusCollection(bool includeLastUpdated = false)
        {
            _includeLastUpdated = includeLastUpdated;
        }

        IEnumerator<SqlDataRecord> IEnumerable<SqlDataRecord>.GetEnumerator()
        {
            // Use ternary operator to choose the appropriate SqlDataRecord initialization
            SqlDataRecord sqlRow = _includeLastUpdated
                ? new SqlDataRecord(
                    new SqlMetaData("Uri", SqlDbType.VarChar, 128),
                    new SqlMetaData("Status", SqlDbType.VarChar, 20),
                    new SqlMetaData("IsPartiallySupported", SqlDbType.Bit),
                    new SqlMetaData("LastUpdated", SqlDbType.DateTimeOffset))
                : new SqlDataRecord(
                    new SqlMetaData("Uri", SqlDbType.VarChar, 128),
                    new SqlMetaData("Status", SqlDbType.VarChar, 20),
                    new SqlMetaData("IsPartiallySupported", SqlDbType.Bit));

            foreach (ResourceSearchParameterStatus status in this)
            {
                sqlRow.SetString(0, status.Uri.OriginalString);
                sqlRow.SetString(1, status.Status.ToString());
                sqlRow.SetSqlBoolean(2, status.IsPartiallySupported);

                if (_includeLastUpdated)
                {
                    if (status.LastUpdated != default(DateTimeOffset))
                    {
                        sqlRow.SetDateTimeOffset(3, status.LastUpdated);
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
