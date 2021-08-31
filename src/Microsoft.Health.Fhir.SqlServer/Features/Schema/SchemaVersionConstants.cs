// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    public static class SchemaVersionConstants
    {
        public const int Min = (int)SchemaVersion.V4;
        public const int Max = (int)SchemaVersion.V18;
        public const int SearchParameterStatusSchemaVersion = (int)SchemaVersion.V6;
        public const int SupportForReferencesWithMissingTypeVersion = (int)SchemaVersion.V7;
        public const int SearchParameterHashSchemaVersion = (int)SchemaVersion.V8;
        public const int PartitionedTables = (int)SchemaVersion.V9;
        public const int SearchParameterSynchronizationVersion = (int)SchemaVersion.V12;
        public const int PurgeHistoryVersion = (int)SchemaVersion.V13;
        public const int SupportsResourceChangeCaptureSchemaVersion = (int)SchemaVersion.V14;
        public const int BulkReindexReturnsFailuresVersion = (int)SchemaVersion.V16;
        public const int AddMinMaxForDateAndStringSearchParamVersion = (int)SchemaVersion.V18;
    }
}
