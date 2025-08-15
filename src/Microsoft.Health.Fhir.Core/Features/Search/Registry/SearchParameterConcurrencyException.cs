// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    /// <summary>
    /// Exception thrown when an optimistic concurrency conflict occurs during search parameter updates.
    /// </summary>
    public class SearchParameterConcurrencyException : Exception
    {
        public SearchParameterConcurrencyException(IEnumerable<string> conflictedUris)
            : base($"Optimistic concurrency conflict detected for search parameters: {string.Join(", ", conflictedUris)}")
        {
            ConflictedUris = conflictedUris?.ToList() ?? new List<string>();
        }

        public SearchParameterConcurrencyException(string message)
            : base(message)
        {
            ConflictedUris = new List<string>();
        }

        public SearchParameterConcurrencyException(string message, Exception innerException)
            : base(message, innerException)
        {
            ConflictedUris = new List<string>();
        }

        /// <summary>
        /// Gets the URIs of search parameters that had concurrency conflicts.
        /// </summary>
        public IReadOnlyList<string> ConflictedUris { get; }
    }
}
