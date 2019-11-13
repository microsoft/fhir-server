// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public static class ListedConformanceStatementExtensions
    {
        public static ListedRestComponent Server(this IEnumerable<ListedRestComponent> restComponents)
        {
            EnsureArg.IsNotNull(restComponents, nameof(restComponents));

            return restComponents.Single(x => x.Mode == ListedCapabilityStatement.ServerMode);
        }
    }
}
