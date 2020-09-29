// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class SaveOutcome
    {
        public SaveOutcome(RawResourceElement rawResource, SaveOutcomeType outcome)
        {
            EnsureArg.IsNotNull(rawResource, nameof(rawResource));

            RawResource = rawResource;
            Outcome = outcome;
        }

        public RawResourceElement RawResource { get; }

        public SaveOutcomeType Outcome { get; }
    }
}
