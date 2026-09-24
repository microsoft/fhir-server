// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Medino;

namespace Microsoft.Health.Fhir.Core.Messages.Conformance
{
    public class ExpandRequest : IRequest<ExpandResponse>
    {
        public ExpandRequest(
            IReadOnlyList<Tuple<string, string>> parameters,
            string resourceId = null)
        {
            Parameters = parameters;
            ResourceId = resourceId;
        }

        public IReadOnlyList<Tuple<string, string>> Parameters { get; } = new List<Tuple<string, string>>();

        public string ResourceId { get; }
    }
}
