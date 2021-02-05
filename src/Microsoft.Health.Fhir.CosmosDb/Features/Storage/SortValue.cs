// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class SortValue
    {
        public SortValue(ISearchValue low, ISearchValue hi)
        {
            EnsureArg.IsNotNull(low, nameof(low));
            EnsureArg.IsNotNull(hi, nameof(hi));

            Low = low;
            Hi = hi;
        }

        public SortValue()
        {
        }

        public ISearchValue Low { get; }

        public ISearchValue Hi { get; }
    }
}
