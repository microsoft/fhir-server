// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema.Messages.Get
{
    public class GetCompatibilityVersionResponse
    {
        public GetCompatibilityVersionResponse(CompatibleVersions versions)
        {
            EnsureArg.IsNotNull(versions, nameof(versions));

            Versions = versions;
        }

        public CompatibleVersions Versions { get; }
    }
}
