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
    /// <summary>
    /// Represents a table-valued parameter.
    /// </summary>
    /// <typeparam name="TRow">The struct type that hold CLR data for a row.</typeparam>
    public abstract class TableValuedParameterDefinition<TRow> : ParameterDefinition<IEnumerable<TRow>>
        where TRow : struct
    {
        private readonly string _tableTypeName;
        private SqlMetaData[] _columnMetadata;

        protected TableValuedParameterDefinition(string parameterName, string tableTypeName)
            : base(parameterName, SqlDbType.Structured, false)
        {
            EnsureArg.IsNotNullOrWhiteSpace(tableTypeName, nameof(tableTypeName));
            _tableTypeName = tableTypeName;
        }

        private SqlMetaData[] ColumnMetadata => _columnMetadata ?? (_columnMetadata = Columns.Select(c => c.Metadata).ToArray());

        /// <summary>
        /// Gets the columns that make up the table type. In order.
        /// </summary>
        protected abstract IEnumerable<Column> Columns { get; }

        protected abstract void FillSqlDataRecord(SqlDataRecord record, TRow rowData);

        public override SqlParameter AddParameter(SqlParameterCollection parameters, IEnumerable<TRow> value)
        {
            // An empty TVP is required to be null.

            value = value.NullIfEmpty();

            return parameters.Add(
                new SqlParameter(Name, SqlDbType.Structured)
                {
                    TypeName = _tableTypeName,
                    Value = value == null ? null : ToDataRecordEnumerable(value),
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
