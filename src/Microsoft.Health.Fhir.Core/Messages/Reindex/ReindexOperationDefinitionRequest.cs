// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Reindex
{
    public class ReindexOperationDefinitionRequest : IRequest<ReindexOperationDefinitionResponse>
    {
        public ReindexOperationDefinitionRequest(string route)
        {
            EnsureArg.IsNotNullOrWhiteSpace(route, nameof(route));

            Route = route;
        }

        public string Route { get; }
    }
}
