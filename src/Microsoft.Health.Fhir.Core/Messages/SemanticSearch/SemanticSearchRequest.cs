// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Medino;

namespace Microsoft.Health.Fhir.Core.Messages.SemanticSearch
{
    /// <summary>
    /// Requests semantic ranking of resources associated with a patient.
    /// </summary>
    public sealed class SemanticSearchRequest : IRequest<SemanticSearchResponse>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SemanticSearchRequest"/> class.
        /// </summary>
        public SemanticSearchRequest(string query, string patientId, int count, IReadOnlyCollection<string> resourceTypes = null)
        {
            Query = EnsureArg.IsNotNullOrWhiteSpace(query, nameof(query));
            PatientId = EnsureArg.IsNotNullOrWhiteSpace(patientId, nameof(patientId));
            Count = EnsureArg.IsGt(count, 0, nameof(count));
            ResourceTypes = resourceTypes?.Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        }

        /// <summary>Gets the natural-language query.</summary>
        public string Query { get; }

        /// <summary>Gets the Patient resource ID used for compartment filtering.</summary>
        public string PatientId { get; }

        /// <summary>Gets the maximum number of results.</summary>
        public int Count { get; }

        /// <summary>Gets the selected FHIR resource types, or an empty collection when all supported types are requested.</summary>
        public IReadOnlyCollection<string> ResourceTypes { get; }
    }
}
