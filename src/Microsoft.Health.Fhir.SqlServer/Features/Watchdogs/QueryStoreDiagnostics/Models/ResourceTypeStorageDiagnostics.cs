// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics.Models
{
    internal sealed class ResourceTypeStorageDiagnostics
    {
        public string SchemaName { get; set; }

        public string TableName { get; set; }

        public string Attribution { get; set; }

        public long? ResourceTypeId { get; set; }

        public string ResourceTypeName { get; set; }

        public long? ApproximateRowCount { get; set; }

        public long? UsedBytes { get; set; }

        public long? ReservedBytes { get; set; }
    }
}
