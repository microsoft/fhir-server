// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Messages.Upsert
{
    public class ResourceUpsertedEvent : INotification
    {
        public ResourceUpsertedEvent(Resource resource, SaveOutcomeType outcome)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            Resource = resource;
            Outcome = outcome;
        }

        public Resource Resource { get; }

        public SaveOutcomeType Outcome { get; }
    }
}
