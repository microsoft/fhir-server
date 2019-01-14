// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.ControlPlane.Core.Features.Persistence
{
    public class UpsertResponse<T>
        where T : class
    {
        public UpsertResponse(T controlPlaneResource, UpsertOutcome outcomeType, string eTag)
        {
            EnsureArg.IsNotNull(controlPlaneResource, nameof(controlPlaneResource));
            Resource = controlPlaneResource;
            OutcomeType = outcomeType;
            ETag = eTag;
        }

        public T Resource { get; }

        public UpsertOutcome OutcomeType { get; }

        public string ETag { get; }
    }
}
