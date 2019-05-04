// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using EnsureThat;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.SqlServer.Server;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema.Model
{
#pragma warning disable SA1402 // File may only contain a single type. Adding all Column-derived types in this files.

    /// <summary>
    /// Represents a typed table column, table-valued parameter column, or a stored procedure parameter.
    /// </summary>
    /// <typeparam name="T">The CLR column type</typeparam>
    public abstract class Column<T>
    {
        private readonly string _defaultParameterName;

        protected Column(string name, SqlDbType type, bool nullable)
            : this(name, nullable)
        {
            Metadata = new SqlMetaData(name, type);
        }

        protected Column(string name, SqlDbType type, bool nullable, long length)
            : this(name, nullable)
        {
            Metadata = new SqlMetaData(name, type, length);
        }

        protected Column(string name, SqlDbType type, bool nullable, byte precision, byte scale)
            : this(name, nullable)
        {
            Metadata = new SqlMetaData(name, type, precision, scale);
        }

        private Column(string name, bool nullable)
        {
            EnsureArg.IsNotNullOrWhiteSpace(name, nameof(name));
            Nullable = nullable;
            _defaultParameterName = $"@{char.ToLowerInvariant(name[0])}{name.Substring(1)}";
        }

        public bool Nullable { get; }

        public SqlMetaData Metadata { get; }

        public static implicit operator string(Column<T> column) => column.ToString();

        public override string ToString() => Metadata.Name;

        public abstract T Read(SqlDataReader reader, int ordinal);

        public SqlParameter AddParameter(SqlParameterCollection parameters, T value, string parameterName)
        {
            EnsureArg.IsNotNull(parameters, nameof(parameters));

            return parameters.Add(
                new SqlParameter(
                    parameterName: parameterName,
                    dbType: Metadata.SqlDbType,
                    size: (int)Metadata.MaxLength,
                    direction: ParameterDirection.Input,
                    isNullable: Nullable,
                    precision: Metadata.Precision,
                    scale: Metadata.Scale,
                    sourceColumn: null,
                    sourceVersion: DataRowVersion.Current,
                    value: value));
        }

        public SqlParameter AddParameterWithDefaultName(SqlParameterCollection parameters, T value) => AddParameter(parameters, value, _defaultParameterName);
    }

    public class IntColumn : Column<int>
    {
        public IntColumn(string name)
            : base(name, SqlDbType.Int, false)
        {
        }

        public override int Read(SqlDataReader reader, int ordinal)
        {
            return reader.GetInt32(Metadata.Name, ordinal);
        }
    }

    public class BigIntColumn : Column<long>
    {
        public BigIntColumn(string name)
            : base(name, SqlDbType.BigInt, false)
        {
        }

        public override long Read(SqlDataReader reader, int ordinal)
        {
            return reader.GetInt64(Metadata.Name, ordinal);
        }
    }

    public class BitColumn : Column<bool>
    {
        public BitColumn(string name)
            : base(name, SqlDbType.Bit, false)
        {
        }

        public override bool Read(SqlDataReader reader, int ordinal)
        {
            return reader.GetBoolean(Metadata.Name, ordinal);
        }
    }

    public class DateTime2Column : Column<DateTime>
    {
        public DateTime2Column(string name, byte scale)
            : base(name, SqlDbType.DateTime2, false, 0, scale)
        {
        }

        public override DateTime Read(SqlDataReader reader, int ordinal)
        {
            return reader.GetDateTime(Metadata.Name, ordinal);
        }
    }

    public class DateTimeOffsetColumn : Column<DateTimeOffset>
    {
        public DateTimeOffsetColumn(string name, byte scale)
            : base(name, SqlDbType.DateTimeOffset, false, 0, scale)
        {
        }

        public override DateTimeOffset Read(SqlDataReader reader, int ordinal)
        {
            return reader.GetDateTimeOffset(Metadata.Name, ordinal);
        }
    }

    public class DecimalColumn : Column<decimal>
    {
        public DecimalColumn(string name, byte precision, byte scale)
            : base(name, SqlDbType.Decimal, false, precision, scale)
        {
        }

        public override decimal Read(SqlDataReader reader, int ordinal)
        {
            return reader.GetDecimal(Metadata.Name, ordinal);
        }
    }

    public class SmallIntColumn : Column<short>
    {
        public SmallIntColumn(string name)
            : base(name, SqlDbType.SmallInt, false)
        {
        }

        public override short Read(SqlDataReader reader, int ordinal)
        {
            return reader.GetInt16(Metadata.Name, ordinal);
        }
    }

    public class TinyIntColumn : Column<byte>
    {
        public TinyIntColumn(string name)
            : base(name, SqlDbType.TinyInt, false)
        {
        }

        public override byte Read(SqlDataReader reader, int ordinal)
        {
            return reader.GetByte(Metadata.Name, ordinal);
        }
    }

    public class NullableIntColumn : Column<int?>
    {
        public NullableIntColumn(string name)
            : base(name, SqlDbType.Int, true)
        {
        }

        public override int? Read(SqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(Metadata.Name, ordinal) ? default(int?) : reader.GetInt32(Metadata.Name, ordinal);
        }
    }

    public class NullableBigIntColumn : Column<long?>
    {
        public NullableBigIntColumn(string name)
            : base(name, SqlDbType.BigInt, true)
        {
        }

        public override long? Read(SqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(Metadata.Name, ordinal) ? default(long?) : reader.GetInt64(Metadata.Name, ordinal);
        }
    }

    public class NullableBitColumn : Column<bool?>
    {
        public NullableBitColumn(string name)
            : base(name, SqlDbType.Bit, true)
        {
        }

        public override bool? Read(SqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(Metadata.Name, ordinal) ? default(bool?) : reader.GetBoolean(Metadata.Name, ordinal);
        }
    }

    public class NullableDateTime2Column : Column<DateTime?>
    {
        public NullableDateTime2Column(string name, byte scale)
            : base(name, SqlDbType.DateTime2, true, 0, scale)
        {
        }

        public override DateTime? Read(SqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(Metadata.Name, ordinal) ? default(DateTime?) : reader.GetDateTime(Metadata.Name, ordinal);
        }
    }

    public class NullableDateTimeOffsetColumn : Column<DateTimeOffset?>
    {
        public NullableDateTimeOffsetColumn(string name, byte scale)
            : base(name, SqlDbType.DateTimeOffset, true, 0, scale)
        {
        }

        public override DateTimeOffset? Read(SqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(Metadata.Name, ordinal) ? default(DateTimeOffset?) : reader.GetDateTimeOffset(Metadata.Name, ordinal);
        }
    }

    public class NullableDecimalColumn : Column<decimal?>
    {
        public NullableDecimalColumn(string name, byte precision, byte scale)
            : base(name, SqlDbType.Decimal, true, precision, scale)
        {
        }

        public override decimal? Read(SqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(Metadata.Name, ordinal) ? default(decimal?) : reader.GetDecimal(Metadata.Name, ordinal);
        }
    }

    public class NullableSmallIntColumn : Column<short?>
    {
        public NullableSmallIntColumn(string name)
            : base(name, SqlDbType.SmallInt, true)
        {
        }

        public override short? Read(SqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(Metadata.Name, ordinal) ? default(short?) : reader.GetInt16(Metadata.Name, ordinal);
        }
    }

    public class NullableTinyIntColumn : Column<byte?>
    {
        public NullableTinyIntColumn(string name)
            : base(name, SqlDbType.TinyInt, true)
        {
        }

        public override byte? Read(SqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(Metadata.Name, ordinal) ? default(byte?) : reader.GetByte(Metadata.Name, ordinal);
        }
    }

    public abstract class StringColumn : Column<string>
    {
        public StringColumn(string name, SqlDbType type, bool nullable, int length)
            : base(name, type, nullable, length)
        {
        }

        public override string Read(SqlDataReader reader, int ordinal)
        {
            return Nullable && reader.IsDBNull(ordinal) ? null : reader.GetString(Metadata.Name, ordinal);
        }
    }

    public class NVarCharColumn : StringColumn
    {
        public NVarCharColumn(string name, int length)
            : base(name, SqlDbType.NVarChar, false, length)
        {
        }
    }

    public class VarCharColumn : StringColumn
    {
        public VarCharColumn(string name, int length)
            : base(name, SqlDbType.NVarChar, false, length)
        {
        }
    }

    public class VarBinaryColumn : Column<Stream>
    {
        public VarBinaryColumn(string name, int length)
            : base(name, SqlDbType.VarBinary, false, length)
        {
        }

        public override Stream Read(SqlDataReader reader, int ordinal)
        {
            return reader.GetStream(Metadata.Name, ordinal);
        }
    }

    public class NullableNVarCharColumn : StringColumn
    {
        public NullableNVarCharColumn(string name, int length)
            : base(name, SqlDbType.NVarChar, true, length)
        {
        }
    }

    public class NullableVarCharColumn : StringColumn
    {
        public NullableVarCharColumn(string name, int length)
            : base(name, SqlDbType.NVarChar, true, length)
        {
        }
    }

    public class NullableVarBinaryColumn : Column<Stream>
    {
        public NullableVarBinaryColumn(string name, int length)
            : base(name, SqlDbType.VarBinary, true, length)
        {
        }

        public override Stream Read(SqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(Metadata.Name, ordinal) ? null : reader.GetStream(Metadata.Name, ordinal);
        }
    }

#pragma warning restore SA1402 // File may only contain a single type
}
