// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.BulkImport
{
    public class GetBulkImportRequest : IRequest<GetBulkImportResponse>
    {
        public GetBulkImportRequest(Uri requestUri, string taskId)
        {
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));
            EnsureArg.IsNotNullOrWhiteSpace(taskId, nameof(taskId));

            RequestUri = requestUri;
            TaskId = taskId;
        }

        public Uri RequestUri { get; }

        public string TaskId { get; }
    }
}
