// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    /// <summary>
    /// Wraps a <see cref="SqlQueryParameterManager"/>, adding the ability to compute a hash of a subset of the parameters.
    /// </summary>
    public class HashingSqlQueryParameterManager
    {
        private readonly SqlQueryParameterManager _inner;
        private readonly HashSet<SqlParameter> _setToHash = new();

        public HashingSqlQueryParameterManager(SqlQueryParameterManager inner)
        {
            EnsureArg.IsNotNull(inner, nameof(inner));
            _inner = inner;
        }

        public bool HasParametersToHash => _setToHash.Count > 0;

        /// <summary>
        /// Add a parameter to the SQL command.
        /// </summary>
        /// <typeparam name="T">The CLR column type</typeparam>
        /// <param name="column">The table column the parameter is bound to.</param>
        /// <param name="value">The parameter value.</param>
        /// <param name="includeInHash">
        /// Whether this parameter should be included in the hash of the overall parameters.
        /// If true, this parameter will prevent other identical queries with a different value for this parameter from re-using the query plan.
        /// </param>
        /// <returns>The SQL parameter.</returns>
        public SqlParameter AddParameter<T>(Column<T> column, T value, bool includeInHash)
        {
            return AddParameter((Column)column, value, includeInHash);
        }

        /// <summary>
        /// Add a parameter to the SQL command.
        /// </summary>
        /// <param name="column">The table column the parameter is bound to.</param>
        /// <param name="value">The parameter value</param>
        /// <param name="includeInHash">
        /// Whether this parameter should be included in the hash of the overall parameters.
        /// If true, this parameter will prevent other identical queries with a different value for this parameter from re-using the query plan.
        /// </param>
        /// <returns>The SQL parameter.</returns>
        public SqlParameter AddParameter(Column column, object value, bool includeInHash)
        {
            SqlParameter parameter = _inner.AddParameter(column, value);
            if (includeInHash)
            {
                _setToHash.Add(parameter);
            }

            return parameter;
        }

        /// <summary>
        /// Add a parameter to the SQL command.
        /// </summary>
        /// <param name="value">The parameter value</param>
        /// <param name="includeInHash">
        /// Whether this parameter should be included in the hash of the overall parameters.
        /// If true, this parameter will prevent other identical queries with a different value for this parameter from re-using the query plan.
        /// </param>
        /// <returns>The SQL parameter.</returns>
        public SqlParameter AddParameter(object value, bool includeInHash)
        {
            SqlParameter parameter = _inner.AddParameter(value);
            if (includeInHash)
            {
                _setToHash.Add(parameter);
            }

            return parameter;
        }

        /// <summary>
        /// Appends a Base64-encoded SHA-256 hash of the parameters currently added to this instance with includeInHash = true
        /// </summary>
        /// <param name="stringBuilder">A string builder to append the hash to.</param>
        public void AppendHash(IndentedStringBuilder stringBuilder)
        {
            IncrementalHash incrementalHash = null;
            Span<byte> buf = stackalloc byte[256];
            int currentBufferIndex = 0;

            foreach (SqlParameter sqlParameter in _setToHash)
            {
                switch (sqlParameter.SqlDbType)
                {
                    case SqlDbType.BigInt:
                        WriteAndAdvance(buf, ref currentBufferIndex, ref incrementalHash, (long)sqlParameter.Value);
                        break;
                    case SqlDbType.Bit:
                        WriteAndAdvance(buf, ref currentBufferIndex, ref incrementalHash, (bool)sqlParameter.Value);
                        break;
                    case SqlDbType.Date:
                    case SqlDbType.DateTime:
                    case SqlDbType.DateTime2:
                    case SqlDbType.SmallDateTime:
                    case SqlDbType.Time:
                        WriteAndAdvance(buf, ref currentBufferIndex, ref incrementalHash, (DateTime)sqlParameter.Value);
                        break;
                    case SqlDbType.DateTimeOffset:
                        WriteAndAdvance(buf, ref currentBufferIndex, ref incrementalHash, (DateTimeOffset)sqlParameter.Value);
                        break;
                    case SqlDbType.Decimal:
                        WriteAndAdvance(buf, ref currentBufferIndex, ref incrementalHash, (decimal)sqlParameter.Value);
                        break;
                    case SqlDbType.Float:
                    case SqlDbType.Real:
                        WriteAndAdvance(buf, ref currentBufferIndex, ref incrementalHash, (double)sqlParameter.Value);
                        break;
                    case SqlDbType.Int:
                        WriteAndAdvance(buf, ref currentBufferIndex, ref incrementalHash, (int)sqlParameter.Value);
                        break;
                    case SqlDbType.SmallInt:
                        WriteAndAdvance(buf, ref currentBufferIndex, ref incrementalHash, (short)sqlParameter.Value);
                        break;
                    case SqlDbType.TinyInt:
                        WriteAndAdvance(buf, ref currentBufferIndex, ref incrementalHash, (byte)sqlParameter.Value);
                        break;
                    case SqlDbType.UniqueIdentifier:
                        WriteAndAdvance(buf, ref currentBufferIndex, ref incrementalHash, (Guid)sqlParameter.Value);
                        break;
                    case SqlDbType.NChar:
                    case SqlDbType.NText:
                    case SqlDbType.VarChar:
                    case SqlDbType.NVarChar:
                    case SqlDbType.Text:
                        WriteAndAdvanceString(buf, ref currentBufferIndex, ref incrementalHash, (string)sqlParameter.Value);
                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected parameter type {sqlParameter.SqlDbType}");
                }
            }

            Span<byte> hashBytes = stackalloc byte[32];

            if (incrementalHash != null)
            {
                if (currentBufferIndex > 0)
                {
                    incrementalHash.AppendData(buf[..currentBufferIndex]);
                }

                incrementalHash.GetCurrentHash(hashBytes);
                incrementalHash.Dispose();
            }
            else
            {
                if (!SHA256.TryHashData(buf[..currentBufferIndex], hashBytes, out _))
                {
                    throw new InvalidOperationException("Failed to hash data");
                }
            }

            Span<char> hashChars = stackalloc char[44]; // 44 since inputLength = 32 and inputLength => (inputLength / 3 * 4) + (((inputLength % 3) != 0) ? 4 : 0) = 44

            if (!Convert.TryToBase64Chars(hashBytes, hashChars, out int hashCharsLength))
            {
                throw new InvalidOperationException("Failed to convert to Base64 chars.");
            }

            stringBuilder.Append(hashChars[..hashCharsLength]);
        }

        private static void WriteAndAdvance<T>(Span<byte> buffer, ref int currentIndex, ref IncrementalHash incrementalHash, T element)
            where T : struct
        {
            int elementLength = Unsafe.SizeOf<T>();
            if (currentIndex + elementLength > buffer.Length)
            {
                incrementalHash ??= IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                incrementalHash.AppendData(buffer[..currentIndex]);
                currentIndex = 0;
                Debug.Assert(buffer.Length >= elementLength, "Initial buffer size is not large enough for the datatypes we are trying to write to it");
            }

#if NET8_0_OR_GREATER
            MemoryMarshal.Write(buffer[currentIndex..], in element);
#else
            MemoryMarshal.Write(buffer[currentIndex..], ref element);
#endif
            currentIndex += elementLength;
        }

        private static void WriteAndAdvanceString(Span<byte> buffer, ref int currentIndex, ref IncrementalHash incrementalHash, string element)
        {
            ReadOnlySpan<byte> elementSpan = MemoryMarshal.AsBytes(element.AsSpan());

            if (currentIndex + elementSpan.Length > buffer.Length)
            {
                incrementalHash ??= IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                incrementalHash.AppendData(buffer[..currentIndex]);
                currentIndex = 0;
            }

            if (currentIndex + elementSpan.Length > buffer.Length)
            {
                // still too big to fit.
                incrementalHash.AppendData(elementSpan);
            }
            else
            {
                elementSpan.CopyTo(buffer);
                currentIndex += elementSpan.Length;
            }
        }
    }
}
