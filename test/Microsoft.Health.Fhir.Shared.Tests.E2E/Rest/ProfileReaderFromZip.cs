// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification.Source;
using Microsoft.Health.Fhir.Core.Features.Validation;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.Rest
{
    /// <summary>
    /// Extract profiles from specified zip folder.
    /// </summary>
    internal sealed class ProfileReaderFromZip : IProvideProfilesForValidation
    {
        private ZipSource _source;

        public ProfileReaderFromZip(string path)
        {
            _source = new ZipSource(path);
        }

        public IEnumerable<ArtifactSummary> ListSummaries()
        {
            return _source.ListSummaries();
        }

        public Resource LoadBySummary(ArtifactSummary summary)
        {
            return _source.LoadBySummary(summary);
        }

        public Resource ResolveByCanonicalUri(string uri)
        {
            return _source.ResolveByCanonicalUri(uri);
        }

        public Resource ResolveByUri(string uri)
        {
            return _source.ResolveByUri(uri);
        }
    }
}
