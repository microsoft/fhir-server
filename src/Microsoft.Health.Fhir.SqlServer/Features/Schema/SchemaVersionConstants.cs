// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    public static class SchemaVersionConstants
    {
        public const int Min = (int)SchemaVersion.V55;
        public const int Max = (int)SchemaVersion.V56;
        public const int MinForUpgrade = (int)SchemaVersion.V52; // this is used for upgrade tests only
        public const int SearchParameterStatusSchemaVersion = (int)SchemaVersion.V6;
        public const int SupportForReferencesWithMissingTypeVersion = (int)SchemaVersion.V7;
        public const int SearchParameterHashSchemaVersion = (int)SchemaVersion.V8;
        public const int PartitionedTables = (int)SchemaVersion.V9;
        public const int SearchParameterSynchronizationVersion = (int)SchemaVersion.V12;
        public const int PurgeHistoryVersion = (int)SchemaVersion.V13;
        public const int SupportsResourceChangeCaptureSchemaVersion = (int)SchemaVersion.V14;
        public const int BulkReindexReturnsFailuresVersion = (int)SchemaVersion.V16;
        public const int AddMinMaxForDateAndStringSearchParamVersion = (int)SchemaVersion.V18;
        public const int SupportsPartitionedResourceChangeDataVersion = (int)SchemaVersion.V20;
        public const int AddPrimaryKeyForResourceTable = (int)SchemaVersion.V25;
        public const int RenamedIndexForResourceTable = (int)SchemaVersion.V26;
        public const int PutCreateWithVersionedUpdatePolicyVersion = (int)SchemaVersion.V27;
        public const int RemoveCountForGexNextTaskStoredProcedure = (int)SchemaVersion.V29;
        public const int PreventUpdatesFromCreatingVersionWhenNoImpact = (int)SchemaVersion.V30;
        public const int SupportParentTask = (int)SchemaVersion.V33;
        public const int ReturnCancelRequestInJobHeartbeat = (int)SchemaVersion.V37;
        public const int TokenOverflow = (int)SchemaVersion.V41;
        public const int Defrag = (int)SchemaVersion.V43;
        public const int ExportTimeTravel = (int)SchemaVersion.V44;
        public const int Merge = (int)SchemaVersion.V50;
        public const int IncrementalImport = (int)SchemaVersion.V53;

        // It is currently used in Azure Healthcare APIs.
        public const int ParameterizedRemovePartitionFromResourceChangesVersion = (int)SchemaVersion.V21;
        public const int SupportsClusteredIdOnResourceChangesVersion = (int)SchemaVersion.V24;
    }
}
