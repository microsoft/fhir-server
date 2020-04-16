// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Search
{
    public class SearchParametersUpdated : INotification
    {
        public SearchParametersUpdated(IReadOnlyCollection<SearchParameterInfo> searchParameters)
        {
            EnsureArg.IsNotNull(searchParameters, nameof(searchParameters));

            SearchParameters = searchParameters;
        }

        public IReadOnlyCollection<SearchParameterInfo> SearchParameters { get; }
    }
}
