// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using EnsureThat;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlQueryParameterManager
    {
        private readonly SqlParameterCollection _parameters;

        private readonly Dictionary<(SqlDbType SqlDbType, long Length, byte Precision, byte Scale, object value), SqlParameter> _lookup =
            new Dictionary<(SqlDbType SqlDbType, long Length, byte Precision, byte Scale, object value), SqlParameter>();

        public SqlQueryParameterManager(SqlParameterCollection parameters)
        {
            EnsureArg.IsNotNull(parameters, nameof(parameters));
            _parameters = parameters;
        }

        public SqlParameter AddParameter<T>(Column<T> column, T value)
        {
            return AddParameter((Column)column, value);
        }

        public SqlParameter AddParameter(Column column, object value)
        {
            var key = (column.Metadata.SqlDbType, column.Metadata.MaxLength, column.Metadata.Precision, column.Metadata.Scale, value);

            if (!_lookup.TryGetValue(key, out var parameter))
            {
                parameter = _parameters.Add(
                    new SqlParameter(
                        parameterName: NextParameterName(),
                        dbType: column.Metadata.SqlDbType,
                        size: (int)column.Metadata.MaxLength,
                        direction: ParameterDirection.Input,
                        isNullable: column.Nullable,
                        precision: column.Metadata.Precision,
                        scale: column.Metadata.Scale,
                        sourceColumn: null,
                        sourceVersion: DataRowVersion.Current,
                        value: value));

                _lookup.Add(key, parameter);
            }

            return parameter;
        }

        public SqlParameter AddParameter(object value)
        {
            return _parameters.AddWithValue(NextParameterName(), value);
        }

        private string NextParameterName() => $"@p{_parameters.Count}";
    }
}
