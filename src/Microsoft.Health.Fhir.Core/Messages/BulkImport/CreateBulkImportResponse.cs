// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Messages.BulkImport
{
    public class CreateBulkImportResponse
    {
        public CreateBulkImportResponse(string taskId)
        {
            EnsureArg.IsNotNullOrWhiteSpace(taskId, nameof(taskId));

            TaskId = taskId;
        }

        public string TaskId { get; }
    }
}
