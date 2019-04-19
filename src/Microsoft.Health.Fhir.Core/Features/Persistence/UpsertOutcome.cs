// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class UpsertOutcome<T>
        where T : class
    {
        public UpsertOutcome(T wrapper, SaveOutcomeType outcomeType)
        {
            EnsureArg.IsNotNull(wrapper, nameof(wrapper));

            Wrapper = wrapper;
            OutcomeType = outcomeType;
        }

        public T Wrapper { get; }

        public SaveOutcomeType OutcomeType { get; }
    }
}
