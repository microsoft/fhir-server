// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Reindex
{
    public class ReindexJobCompletedRequest : IRequest<ReindexJobCompletedResponse>
    {
        public ReindexJobCompletedRequest(IReadOnlyCollection<string> searchParameterUrls)
        {
            EnsureArg.IsNotNull(searchParameterUrls, nameof(searchParameterUrls));

            SearchParameterUris = searchParameterUrls;
        }

        public IReadOnlyCollection<string> SearchParameterUris { get; }
    }
}
