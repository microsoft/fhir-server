// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Messages.Upsert
{
    public class UpsertResourceResponse
    {
        public UpsertResourceResponse(SaveOutcome outcome)
        {
            EnsureArg.IsNotNull(outcome, nameof(outcome));

            Outcome = outcome;
        }

        public SaveOutcome Outcome { get; }
    }
}
