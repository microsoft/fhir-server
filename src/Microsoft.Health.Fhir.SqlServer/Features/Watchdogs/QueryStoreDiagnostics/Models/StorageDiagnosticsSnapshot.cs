// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics.Models
{
    internal sealed class StorageDiagnosticsSnapshot
    {
        internal StorageDiagnosticsSnapshot(
            IReadOnlyList<TableStorageDiagnostics> tables,
            IReadOnlyList<IndexStorageDiagnostics> indexes,
            IReadOnlyList<ResourceTypeStorageDiagnostics> resourceTypes)
        {
            Tables = tables;
            Indexes = indexes;
            ResourceTypes = resourceTypes;
        }

        internal IReadOnlyList<TableStorageDiagnostics> Tables { get; }

        internal IReadOnlyList<IndexStorageDiagnostics> Indexes { get; }

        internal IReadOnlyList<ResourceTypeStorageDiagnostics> ResourceTypes { get; }

        internal static async Task<StorageDiagnosticsSnapshot> ReadAsync(DbDataReader reader, CancellationToken cancellationToken)
        {
            var tables = new List<TableStorageDiagnostics>();
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(new TableStorageDiagnostics
                {
                    SchemaName = reader.GetString(0),
                    TableName = reader.GetString(1),
                    ApproximateRowCount = ReadNullableInt64(reader, 2),
                    UsedBytes = ReadNullableInt64(reader, 3),
                    ReservedBytes = ReadNullableInt64(reader, 4),
                });
            }

            if (!await reader.NextResultAsync(cancellationToken))
            {
                throw new InvalidOperationException("Storage diagnostics did not return the index result set.");
            }

            var indexes = new List<IndexStorageDiagnostics>();
            while (await reader.ReadAsync(cancellationToken))
            {
                indexes.Add(new IndexStorageDiagnostics
                {
                    SchemaName = reader.GetString(0),
                    TableName = reader.GetString(1),
                    IndexId = reader.GetInt32(2),
                    IndexName = ReadNullableString(reader, 3),
                    ApproximateIndexRowCount = ReadNullableInt64(reader, 4),
                    UsedBytes = ReadNullableInt64(reader, 5),
                    ReservedBytes = ReadNullableInt64(reader, 6),
                });
            }

            if (!await reader.NextResultAsync(cancellationToken))
            {
                throw new InvalidOperationException("Storage diagnostics did not return the resource type result set.");
            }

            var resourceTypes = new List<ResourceTypeStorageDiagnostics>();
            while (await reader.ReadAsync(cancellationToken))
            {
                resourceTypes.Add(new ResourceTypeStorageDiagnostics
                {
                    SchemaName = reader.GetString(0),
                    TableName = reader.GetString(1),
                    Attribution = reader.GetString(2),
                    ResourceTypeId = ReadNullableInt64(reader, 3),
                    ResourceTypeName = ReadNullableString(reader, 4),
                    ApproximateRowCount = ReadNullableInt64(reader, 5),
                    UsedBytes = ReadNullableInt64(reader, 6),
                    ReservedBytes = ReadNullableInt64(reader, 7),
                });
            }

            if (await reader.NextResultAsync(cancellationToken))
            {
                throw new InvalidOperationException("Storage diagnostics returned an unexpected result set.");
            }

            return new StorageDiagnosticsSnapshot(tables, indexes, resourceTypes);
        }

        private static long? ReadNullableInt64(DbDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

        private static string ReadNullableString(DbDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
