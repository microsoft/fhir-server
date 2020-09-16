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
        public SaveOutcome(ResourceElement resource, SaveOutcomeType outcome)
        {
            EnsureArg.IsNotNull(resource);

            Resource = resource;
            Outcome = outcome;
        }

        public ResourceElement Resource { get; }

        public SaveOutcomeType Outcome { get; }
    }
}
