// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    /// <summary>
    /// Represents a group of chained or reverse-chained search parameters that share the same
    /// reference search parameter at the first level. By grouping them, the expensive reference
    /// CTE (ReferenceSearchParam JOIN Resource) is generated only once and reused for all
    /// leaf searches in the group.
    /// </summary>
    public class ChainSearchGroup
    {
        /// <summary>
        /// Gets the grouping key that identifies the shared reference lookup.
        /// For forward chains: the reference parameter code (e.g., "subject" or "subject:Patient").
        /// For reverse chains: "resourceType:referenceParam" (e.g., "Coverage:beneficiary").
        /// </summary>
        public string GroupKey { get; }

        /// <summary>
        /// Gets whether this is a reverse chain (_has) group.
        /// </summary>
        public bool IsReverseChain { get; }

        /// <summary>
        /// Gets the individual search entries in this group. Each entry represents a leaf
        /// search parameter and its value that filters the referenced resources.
        /// </summary>
        public List<ChainSearchEntry> Entries { get; } = new();

        public ChainSearchGroup(string groupKey, bool isReverseChain)
        {
            GroupKey = groupKey;
            IsReverseChain = isReverseChain;
        }

        /// <summary>
        /// Groups chained search parameters by their first-level reference parameter.
        /// Parameters that share the same reference lookup will be in the same group.
        /// </summary>
        public static List<ChainSearchGroup> GroupChainedParameters(
            IDictionary<string, IList<string>> chainedParameters)
        {
            var groups = new Dictionary<string, ChainSearchGroup>();

            foreach (var kvp in chainedParameters)
            {
                var parts = kvp.Key.Split('.', 2);
                if (parts.Length < 2)
                {
                    continue;
                }

                // The first part is the reference param (e.g., "subject" or "subject:Patient")
                var refParam = parts[0];
                var remainingChain = parts[1];

                // Group key is the reference param (normalized)
                var groupKey = refParam.ToLowerInvariant();

                if (!groups.TryGetValue(groupKey, out var group))
                {
                    group = new ChainSearchGroup(refParam, isReverseChain: false);
                    groups[groupKey] = group;
                }

                foreach (var value in kvp.Value)
                {
                    group.Entries.Add(new ChainSearchEntry(
                        fullParameterName: kvp.Key,
                        remainingChain: remainingChain,
                        value: value,
                        referenceParamCode: refParam));
                }
            }

            return groups.Values.ToList();
        }

        /// <summary>
        /// Groups reverse-chained (_has) search parameters by their resource type and reference parameter.
        /// Parameters that share the same reference lookup will be in the same group.
        /// </summary>
        public static List<ChainSearchGroup> GroupReversedChainedParameters(
            IDictionary<string, IList<string>> reversedChainedParameters)
        {
            var groups = new Dictionary<string, ChainSearchGroup>();

            foreach (var kvp in reversedChainedParameters)
            {
                // Format: _has:<resourceType>:<referenceParam>:<searchParam>
                var parts = kvp.Key.Split(':', 4);
                if (parts.Length < 4)
                {
                    continue;
                }

                var resourceType = parts[1];
                var referenceParam = parts[2];
                var searchParam = parts[3];

                // Group key is "resourceType:referenceParam"
                var groupKey = $"{resourceType}:{referenceParam}".ToLowerInvariant();

                if (!groups.TryGetValue(groupKey, out var group))
                {
                    group = new ChainSearchGroup($"{resourceType}:{referenceParam}", isReverseChain: true);
                    groups[groupKey] = group;
                }

                foreach (var value in kvp.Value)
                {
                    group.Entries.Add(new ChainSearchEntry(
                        fullParameterName: kvp.Key,
                        remainingChain: searchParam,
                        value: value,
                        referenceParamCode: referenceParam,
                        sourceResourceType: resourceType));
                }
            }

            return groups.Values.ToList();
        }
    }

    /// <summary>
    /// A single search entry within a chain group. Represents one leaf search parameter
    /// and its value to be applied to the referenced resources.
    /// </summary>
    public class ChainSearchEntry
    {
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

        public ChainSearchEntry(string fullParameterName, string remainingChain, string value, string referenceParamCode, string? sourceResourceType = null)
        {
            FullParameterName = fullParameterName;
            RemainingChain = remainingChain;
            Value = value;
            ReferenceParamCode = referenceParamCode;
            SourceResourceType = sourceResourceType;
        }
    }
}
