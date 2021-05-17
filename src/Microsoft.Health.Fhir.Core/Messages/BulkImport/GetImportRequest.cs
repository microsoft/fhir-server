// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Import
{
    public class GetImportRequest : IRequest<GetImportResponse>
    {
        public GetImportRequest(Uri requestUri, string taskId)
        {
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));
            EnsureArg.IsNotNullOrWhiteSpace(taskId, nameof(taskId));

            RequestUri = requestUri;
            TaskId = taskId;
        }

        /// <summary>
        /// Request uri for get request
        /// </summary>
        public Uri RequestUri { get; }

        /// <summary>
        /// Import task id
        /// </summary>
        public string TaskId { get; }
    }
}
