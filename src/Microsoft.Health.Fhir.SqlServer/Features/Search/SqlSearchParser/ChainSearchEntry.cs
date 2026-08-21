// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    /// <summary>
    /// A single search entry within a chain group. Represents one leaf search parameter
    /// and its value to be applied to the referenced resources.
    /// </summary>
    public class ChainSearchEntry
    {
        public ChainSearchEntry(string fullParameterName, string remainingChain, string value, string referenceParamCode, string? sourceResourceType = null)
        {
            FullParameterName = fullParameterName;
            RemainingChain = remainingChain;
            Value = value;
            ReferenceParamCode = referenceParamCode;
            SourceResourceType = sourceResourceType;
        }

        /// <summary>
        /// Gets the full original parameter name (e.g., "subject:Patient.name" or "_has:Coverage:beneficiary:identifier").
        /// </summary>
        public string FullParameterName { get; }

        /// <summary>
        /// Gets the remaining chain after the first reference lookup (e.g., "name" or "organization:Organization.name").
        /// </summary>
        public string RemainingChain { get; }

        /// <summary>
        /// Gets the search value.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Gets the first-level reference parameter code (e.g., "subject" or "beneficiary").
        /// </summary>
        public string ReferenceParamCode { get; }

        /// <summary>
        /// Gets the source resource type for reverse chains (e.g., "Coverage"). Null for forward chains.
        /// </summary>
        public string? SourceResourceType { get; }
    }
}
