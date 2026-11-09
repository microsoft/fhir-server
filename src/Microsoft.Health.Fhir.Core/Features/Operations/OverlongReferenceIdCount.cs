// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    /// <summary>
    /// The number of resources of a type, written by a background job, with a reference whose resource id is longer than the
    /// maximum resource id length for a search parameter. Saved in job results and returned as an issue in the job status.
    /// </summary>
    public class OverlongReferenceIdCount
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OverlongReferenceIdCount"/> class.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="searchParameterCode">The code of the search parameter.</param>
        /// <param name="maxResourceIdLength">The maximum resource id length the ids were truncated to.</param>
        /// <param name="count">The number of affected resources.</param>
        [JsonConstructor]
        public OverlongReferenceIdCount(string resourceType, string searchParameterCode, int maxResourceIdLength, long count)
        {
            ResourceType = resourceType;
            SearchParameterCode = searchParameterCode;
            MaxResourceIdLength = maxResourceIdLength;
            Count = count;
        }

        /// <summary>
        /// Gets the resource type.
        /// </summary>
        [JsonProperty("resourceType")]
        public string ResourceType { get; }

        /// <summary>
        /// Gets the code of the search parameter.
        /// </summary>
        [JsonProperty("searchParameterCode")]
        public string SearchParameterCode { get; }

        /// <summary>
        /// Gets the maximum resource id length the ids were truncated to.
        /// </summary>
        [JsonProperty("maxResourceIdLength")]
        public int MaxResourceIdLength { get; }

        /// <summary>
        /// Gets or sets the number of affected resources.
        /// </summary>
        [JsonProperty("count")]
        public long Count { get; set; }

        /// <summary>
        /// Adds the overlong references of a written resource to <paramref name="counts"/>, counting the resource once per search parameter.
        /// Callers that share <paramref name="counts"/> across threads must synchronize access.
        /// </summary>
        /// <param name="counts">The counts to add to.</param>
        /// <param name="resource">The written resource.</param>
        public static void Add(ICollection<OverlongReferenceIdCount> counts, ResourceWrapper resource)
        {
            EnsureArg.IsNotNull(counts, nameof(counts));

            if (resource == null || resource.IsHistory)
            {
                return;
            }

            var parameters = LongReferenceIdIssue.GetOverlongReferences(resource.SearchIndices)
                .Select(item => (item.Entry.SearchParameter.Code, MaxLength: item.Reference.MaxResourceIdLength.Value))
                .Distinct();

            foreach ((string code, int maxLength) in parameters)
            {
                Add(counts, new OverlongReferenceIdCount(resource.ResourceTypeName, code, maxLength, 1));
            }
        }

        /// <summary>
        /// Adds the counts in <paramref name="source"/> to <paramref name="target"/>.
        /// </summary>
        /// <param name="target">The counts to add to.</param>
        /// <param name="source">The counts to add.</param>
        public static void Merge(ICollection<OverlongReferenceIdCount> target, IEnumerable<OverlongReferenceIdCount> source)
        {
            EnsureArg.IsNotNull(target, nameof(target));

            foreach (OverlongReferenceIdCount count in source ?? Enumerable.Empty<OverlongReferenceIdCount>())
            {
                Add(target, count);
            }
        }

        private static void Add(ICollection<OverlongReferenceIdCount> counts, OverlongReferenceIdCount count)
        {
            if (count?.ResourceType == null || count.SearchParameterCode == null)
            {
                return;
            }

            OverlongReferenceIdCount existing = counts.FirstOrDefault(c =>
                string.Equals(c.ResourceType, count.ResourceType, StringComparison.Ordinal) &&
                string.Equals(c.SearchParameterCode, count.SearchParameterCode, StringComparison.Ordinal) &&
                c.MaxResourceIdLength == count.MaxResourceIdLength);

            if (existing == null)
            {
                counts.Add(new OverlongReferenceIdCount(count.ResourceType, count.SearchParameterCode, count.MaxResourceIdLength, count.Count));
            }
            else
            {
                existing.Count += count.Count;
            }
        }
    }
}
