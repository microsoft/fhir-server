// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using EnsureThat;
using Microsoft.SqlServer.Server;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema.Model
{
    public abstract class TableValuedParameterDefinition<TRow> : ParameterDefinition<IEnumerable<TRow>>
    {
        private readonly string _tableTypeName;
        private SqlMetaData[] _columnMetadata;

        protected TableValuedParameterDefinition(string tableTypeName)
            : base(SqlDbType.Structured, false)
        {
            EnsureArg.IsNotNullOrWhiteSpace(tableTypeName, nameof(tableTypeName));
            _tableTypeName = tableTypeName;
        }

#pragma warning disable CA1819 // Properties should not return arrays
        protected abstract Column[] Columns { get; }
#pragma warning restore CA1819 // Properties should not return arrays

        private SqlMetaData[] ColumnMetadata => _columnMetadata ?? (_columnMetadata = Columns.Select(c => c.Metadata).ToArray());

        protected abstract void FillSqlDataRecord(SqlDataRecord record, TRow rowData);

        public override SqlParameter AddParameter(SqlParameterCollection parameters, IEnumerable<TRow> value, string parameterName)
        {
            return parameters.Add(
                new SqlParameter(parameterName, SqlDbType.Structured)
                {
                    TypeName = _tableTypeName,
                    Value = ToDataRecordEnumerable(value).NullIfEmpty(),
                });
        }

        private IEnumerable<SqlDataRecord> ToDataRecordEnumerable(IEnumerable<TRow> rows)
        {
            var sqlDataRecord = new SqlDataRecord(ColumnMetadata);
            foreach (var row in rows)
            {
                FillSqlDataRecord(sqlDataRecord, row);

                // deliberately not allocating a new SqlDataRecord instance per row.
                yield return sqlDataRecord;
            }
        }
    }
}
