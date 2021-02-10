// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification.Source;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Validation
{
    public sealed class NoopProvideProfileValidation : IProvideProfilesForValidation
    {
        public NoopProvideProfileValidation()
        {
        }

        public IEnumerable<ArtifactSummary> ListSummaries()
        {
            return new List<ArtifactSummary>();
        }

        public Resource LoadBySummary(ArtifactSummary summary) => null;

        public Resource ResolveByCanonicalUri(string uri) => null;

        public Resource ResolveByUri(string uri) => null;
    }
}
