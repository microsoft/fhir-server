// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics.Models
{
    internal sealed class IndexStorageDiagnostics
    {
        public string SchemaName { get; set; }

        public string TableName { get; set; }

        public int IndexId { get; set; }

        public string IndexName { get; set; }

        public long? ApproximateIndexRowCount { get; set; }

        public long? UsedBytes { get; set; }

        public long? ReservedBytes { get; set; }
    }
}
