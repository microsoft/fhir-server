// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class SaveOutcome
    {
        public SaveOutcome(Resource resource, SaveOutcomeType outcome)
        {
            EnsureArg.IsNotNull(resource);

            Resource = resource;
            Outcome = outcome;
        }

        public Resource Resource { get; }

        public SaveOutcomeType Outcome { get; }
    }
}
