// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema.Messages.Get
{
    public class GetCurrentVersionResponse
    {
        public GetCurrentVersionResponse(IList<CurrentVersionInformation> currentVersions)
        {
            EnsureArg.IsNotNull(currentVersions, nameof(currentVersions));

            CurrentVersions = currentVersions;
        }

        public IList<CurrentVersionInformation> CurrentVersions { get; }
    }
}
