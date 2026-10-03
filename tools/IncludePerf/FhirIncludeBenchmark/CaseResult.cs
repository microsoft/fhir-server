// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Internal.Fhir.IncludePerf.Benchmark
{
    /// <summary>
    /// Latency percentiles for one benchmark case.
    /// </summary>
    internal sealed class CaseResult
    {
        public string Name { get; set; }

        public string Group { get; set; }

        public string Auth { get; set; }

        public string PatientId { get; set; }

        public string PatientClass { get; set; }

        public string PathAndQuery { get; set; }

        public string Notes { get; set; }

        public int Iterations { get; set; }

        public int Errors { get; set; }

        public string FirstError { get; set; }

        public double MeanMs { get; set; }

        public double MinMs { get; set; }

        public double P50Ms { get; set; }

        public double P90Ms { get; set; }

        public double P95Ms { get; set; }

        public double P99Ms { get; set; }

        public double MaxMs { get; set; }

        /// <summary>
        /// Gets or sets the number of entries in the returned bundle. PR 5683 intentionally removes
        /// out-of-compartment resources from include results, so this count is expected to DROP on the branch
        /// for leaking cases. Latency must be interpreted alongside it - a case that got faster only because
        /// it returned fewer rows is not a win, and a case that got slower while returning fewer rows is a
        /// real regression.
        /// </summary>
        public int EntryCount { get; set; }

        /// <summary>
        /// Gets or sets the number of bundle entries with search mode "include".
        /// </summary>
        public int IncludeEntryCount { get; set; }

        /// <summary>
        /// Gets or sets the number of bundle entries with search mode "match".
        /// </summary>
        public int MatchEntryCount { get; set; }

        public long ResponseBytes { get; set; }
    }
}
