// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Reindex
{
    public class CreateReindexRequest : IRequest<CreateReindexResponse>
    {
        public CreateReindexRequest(int? maximumConcurrency = null, string scope = null)
        {
            MaximumConcurrency = maximumConcurrency;
            Scope = scope;
        }

        public int? MaximumConcurrency { get; }

        public string Scope { get; }
    }
}
