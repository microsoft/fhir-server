// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Messages.Export
{
    public class ExportResponse
    {
        public ExportResponse(string id, bool exportJobQueued)
        {
            EnsureArg.IsNotNullOrEmpty(id, nameof(id));

            Id = id;
            ExportJobQueued = exportJobQueued;
        }

        public string Id { get; }

        public bool ExportJobQueued { get; }
    }
}
