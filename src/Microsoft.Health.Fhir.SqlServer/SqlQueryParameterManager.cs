// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Data.SqlClient;

namespace Microsoft.Health.Fhir.SqlServer
{
    public class SqlQueryParameterManager
    {
        private readonly SqlParameterCollection _parameterCollection;
        private int _parameterSuffix;

        public SqlQueryParameterManager(SqlParameterCollection parameterCollection)
        {
            _parameterCollection = parameterCollection;
        }

        public string CreateParameter(object value, SqlDbType? sqlDbType = null, int? length = null)
        {
            SqlParameter sqlParameter = _parameterCollection.AddWithValue("@p" + _parameterSuffix++, value);

            if (sqlDbType != null)
            {
                sqlParameter.SqlDbType = sqlDbType.Value;
            }

            if (length != null)
            {
                sqlParameter.Size = length.Value;
            }

            return sqlParameter.ParameterName;
        }
    }
}
