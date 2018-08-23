// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    internal abstract class SearchValuesExtractor<TResource> : ISearchValuesExtractor
        where TResource : Resource
    {
        IReadOnlyCollection<ISearchValue> ISearchValuesExtractor.Extract(Resource resource)
        {
            EnsureArg.IsOfType(resource, typeof(TResource), nameof(resource));

            return Extract((TResource)resource);
        }

        internal abstract IReadOnlyCollection<ISearchValue> Extract(TResource resource);
    }
}
