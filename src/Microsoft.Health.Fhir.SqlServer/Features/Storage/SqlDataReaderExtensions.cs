// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Provides wrappers around Get* methods on <see cref="SqlDataReader"/> that perform debug checks
    /// verifying that the type and name of the field match the ordinal.
    /// </summary>
    public static class SqlDataReaderExtensions
    {
        public static bool GetBoolean(this SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            CheckFieldNameAndType(dataReader, fieldName, fieldOrdinal, typeof(bool));
            return dataReader.GetBoolean(fieldOrdinal);
        }

        public static byte GetByte(this SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            CheckFieldNameAndType(dataReader, fieldName, fieldOrdinal, typeof(byte));
            return dataReader.GetByte(fieldOrdinal);
        }

        public static long GetBytes(this SqlDataReader dataReader, string fieldName, int fieldOrdinal, long fieldOffset, byte[] buffer, int bufferOffset, int length)
        {
            CheckFieldNameAndType(dataReader, fieldName, fieldOrdinal, typeof(byte[]));
            return dataReader.GetBytes(fieldOrdinal, fieldOffset, buffer, bufferOffset, length);
        }

        public static Stream GetStream(this SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            CheckFieldNameAndType(dataReader, fieldName, fieldOrdinal, typeof(byte[]));
            return dataReader.GetStream(fieldOrdinal);
        }

        public static long GetChars(this SqlDataReader dataReader, string fieldName, int fieldOrdinal, long fieldoffset, char[] buffer, int bufferOffset, int length)
        {
            CheckFieldNameAndType(dataReader, fieldName, fieldOrdinal, typeof(long));
            return dataReader.GetChars(fieldOrdinal, fieldoffset, buffer, bufferOffset, length);
        }

        public static DateTime GetDateTime(this SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            CheckFieldNameAndType(dataReader, fieldName, fieldOrdinal, typeof(DateTime));
            return dataReader.GetDateTime(fieldOrdinal);
        }

        public static DateTimeOffset GetDateTimeOffset(this SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            CheckFieldNameAndType(dataReader, fieldName, fieldOrdinal, typeof(DateTimeOffset));
            return dataReader.GetDateTimeOffset(fieldOrdinal);
        }

        public static decimal GetDecimal(this SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            CheckFieldNameAndType(dataReader, fieldName, fieldOrdinal, typeof(decimal));
            return dataReader.GetDecimal(fieldOrdinal);
        }

        public static double GetDouble(this SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            CheckFieldNameAndType(dataReader, fieldName, fieldOrdinal, typeof(double));
            return dataReader.GetDouble(fieldOrdinal);
        }

        public static Type GetFieldType(this SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            CheckFieldName(dataReader, fieldName, fieldOrdinal);
            return dataReader.GetFieldType(fieldOrdinal);
        }

        public static float GetFloat(this SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            CheckFieldNameAndType(dataReader, fieldName, fieldOrdinal, typeof(float));
            return dataReader.GetFloat(fieldOrdinal);
        }

        public static Guid GetGuid(this SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            CheckFieldNameAndType(dataReader, fieldName, fieldOrdinal, typeof(Guid));
            return dataReader.GetGuid(fieldOrdinal);
        }

        public static short GetInt16(this SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            CheckFieldNameAndType(dataReader, fieldName, fieldOrdinal, typeof(short));
            return dataReader.GetInt16(fieldOrdinal);
        }

        public static int GetInt32(this SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            CheckFieldNameAndType(dataReader, fieldName, fieldOrdinal, typeof(int));
            return dataReader.GetInt32(fieldOrdinal);
        }

        public static long GetInt64(this SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            CheckFieldNameAndType(dataReader, fieldName, fieldOrdinal, typeof(long));
            return dataReader.GetInt64(fieldOrdinal);
        }

        public static string GetString(this SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            CheckFieldNameAndType(dataReader, fieldName, fieldOrdinal, typeof(string));
            return dataReader.GetString(fieldOrdinal);
        }

        public static object GetValue(this SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            CheckFieldName(dataReader, fieldName, fieldOrdinal);
            return dataReader.GetValue(fieldOrdinal);
        }

        public static bool IsDBNull(this SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            CheckFieldName(dataReader, fieldName, fieldOrdinal);
            return dataReader.IsDBNull(fieldOrdinal);
        }

        [Conditional("DEBUG")]
        private static void CheckFieldNameAndType(SqlDataReader dataReader, string fieldName, int fieldOrdinal, Type expectedType)
        {
            Type actualType = dataReader.GetFieldType(fieldName, fieldOrdinal); // ends up calling CheckFieldName
            if (actualType != expectedType)
            {
                throw new InvalidOperationException($"Field at ordinal {fieldOrdinal} was expected to be of type {expectedType.Name} but was instead of type {actualType.Name}");
            }
        }

        [Conditional("DEBUG")]
        private static void CheckFieldName(SqlDataReader dataReader, string fieldName, int fieldOrdinal)
        {
            string actualName = dataReader.GetName(fieldOrdinal);
            if (!actualName.Equals(fieldName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Field at ordinal {fieldOrdinal} was expected to have name {fieldName} but instead had name {actualName}");
            }
        }
    }
}
