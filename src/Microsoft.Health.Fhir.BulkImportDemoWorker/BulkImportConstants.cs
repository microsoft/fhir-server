// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public static class BulkImportConstants
    {
        public const int KB = 1024;
        public const int MB = KB * 1024;
        public const int DefaultConcurrentCount = 5;
        public const int DefaultBlockDownloadTimeoutInSeconds = 30;
        public const int RetryDelayInSecconds = 3;
        public const int DefaultBlockDownloadTimeoutRetryCount = 3;
        public const int BlockBufferSize = 32 * MB;
    }
}
