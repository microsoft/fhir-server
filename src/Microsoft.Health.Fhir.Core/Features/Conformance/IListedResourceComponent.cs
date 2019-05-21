// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public interface IListedResourceComponent
    {
        bool? UpdateCreate { get; set; }

        bool? ConditionalUpdate { get; set; }

        bool? ConditionalCreate { get; set; }

        bool? ReadHistory { get; set; }

        IList<string> SearchRevInclude { get; }

        IList<string> SearchInclude { get; }

        void AddResourceVersionPolicy(string policy);
    }
}
