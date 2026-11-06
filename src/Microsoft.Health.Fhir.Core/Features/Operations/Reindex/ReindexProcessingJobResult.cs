// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexProcessingJobResult
    {
        /// <summary>
        /// The maximum number of warnings kept in a result. Further warnings are only counted.
        /// </summary>
        public const int MaxWarnings = 100;

        /// <summary>
        /// Succeeded reindexed resource count
        /// </summary>
        public long SucceededResourceCount { get; set; }

        /// <summary>
        /// Critical error during data processing.
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Non-critical issues, such as search values that could not be indexed. At most <see cref="MaxWarnings"/>.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<string> Warnings { get; set; }

        /// <summary>
        /// Number of warnings not kept in <see cref="Warnings"/> because of <see cref="MaxWarnings"/>.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public long OmittedWarningCount { get; set; }

        /// <summary>
        /// Adds warnings, keeping at most <see cref="MaxWarnings"/> and counting the rest.
        /// </summary>
        /// <param name="warnings">The warnings to add.</param>
        public void AddWarnings(IEnumerable<string> warnings)
        {
            if (warnings == null)
            {
                return;
            }

            List<string> kept = null;
            foreach (string warning in warnings)
            {
                if ((kept?.Count ?? Warnings?.Count ?? 0) < MaxWarnings)
                {
                    kept ??= Warnings == null ? new List<string>() : new List<string>(Warnings);
                    kept.Add(warning);
                }
                else
                {
                    OmittedWarningCount++;
                }
            }

            if (kept != null)
            {
                Warnings = kept;
            }
        }
    }
}
