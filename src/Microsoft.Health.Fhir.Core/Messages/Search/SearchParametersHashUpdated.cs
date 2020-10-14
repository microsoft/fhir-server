// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Search
{
    public class SearchParametersHashUpdated : INotification
    {
        public SearchParametersHashUpdated(Dictionary<string, string> updatedHashMap)
        {
            EnsureArg.IsNotNull(updatedHashMap, nameof(updatedHashMap));

            UpdatedHashMap = updatedHashMap;
        }

        public Dictionary<string, string> UpdatedHashMap { get; }
    }
}
