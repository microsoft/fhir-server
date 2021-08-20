// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Messages.Import
{
    public class CreateImportResponse
    {
        public CreateImportResponse(string taskId)
        {
            EnsureArg.IsNotNullOrWhiteSpace(taskId, nameof(taskId));

            TaskId = taskId;
        }

        /// <summary>
        ///  Created task id
        /// </summary>
        public string TaskId { get; }
    }
}
