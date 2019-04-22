// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Messages.Export
{
    public class CreateExportResponse
    {
        public CreateExportResponse(string id)
        {
            EnsureArg.IsNotNullOrWhiteSpace(id, nameof(id));

            JobId = id;
        }

        public string JobId { get; }
    }
}
