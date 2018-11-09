// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Search
{
    public class CompartmentResourceRequest : IRequest<CompartmentResourceResponse>
    {
        public CompartmentResourceRequest(string compartmentType, string compartmentId, string resourceType, IReadOnlyList<Tuple<string, string>> queries)
        {
            ResourceType = resourceType;
            CompartmentType = compartmentType;
            CompartmentId = compartmentId;
            Queries = queries;
        }

        public string ResourceType { get; }

        public string CompartmentType { get; }

        public string CompartmentId { get; }

        public IReadOnlyList<Tuple<string, string>> Queries { get; }
    }
}
