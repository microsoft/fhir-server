// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.GraphQl
{
    public class GraphQlResponse
    {
        public GraphQlResponse(IEnumerable<ResourceElement> resourceElements)
        {
            EnsureArg.IsNotNull(resourceElements, nameof(resourceElements));
            ResourceElements = resourceElements;
        }

        public IEnumerable<ResourceElement> ResourceElements { get; }
    }
}
