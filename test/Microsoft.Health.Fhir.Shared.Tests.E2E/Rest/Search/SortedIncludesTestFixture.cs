// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    /// <summary>
    /// Creates the resources used by <see cref="SortedIncludesTests"/>.
    /// </summary>
    /// <remarks>
    /// The data is shaped specifically so that, for a search sorted by <c>date</c>, the resources on any given
    /// page of matches do NOT form a contiguous range of resource surrogate IDs, and the first and last match of
    /// a page are not the page's minimum and maximum surrogate ID.
    /// <para>
    /// Resources are created strictly one at a time so that creation order equals ascending resource surrogate ID
    /// order. The date assigned to each resource is derived from a permutation of the creation order, so date order
    /// interleaves with surrogate ID order. Monotonic data (such as resources created in date order) cannot detect a
    /// surrogate ID range that is derived from the first and last match of a page.
    /// </para>
    /// </remarks>
    public class SortedIncludesTestFixture : HttpIntegrationTestFixture
    {
        /// <summary>
        /// Number of DiagnosticReport/Observation/Encounter triples in the "sorted" data set.
        /// </summary>
        public const int SortedSetSize = 10;

        /// <summary>
        /// Number of DiagnosticReport/Observation/Encounter triples in the "phase" data set. The set is sized so
        /// that, with a page size of 3, each of the two sort phases spans at least two whole pages of matches and
        /// one page falls on the phase boundary, for both an ascending and a descending sort.
        /// </summary>
        public const int PhaseSetSize = 15;

        private const string TagSystem = "http://microsoft.com/fhir/test/sorted-includes";
        private const string CodeSystem = "http://microsoft.com/fhir/test/sorted-includes-code";
        private const string EncounterClassSystem = "http://terminology.hl7.org/CodeSystem/v3-ActCode";

        /// <summary>
        /// Position <c>r</c> holds the creation index of the resource that is at rank <c>r</c> when the
        /// "sorted" data set is ordered by date descending. The permutation is intentionally non-monotonic.
        /// With a page size of 3 the pages of matches are {4,0,8}, {6,1,3}, {7,2,5} and {9}; the first page's
        /// surrogate ID endpoints are 4 and 8, which excludes match 0 and spans non-matches 5, 6 and 7.
        /// </summary>
        private static readonly int[] SortedSetDescendingRankToCreationIndex = { 4, 0, 8, 6, 1, 3, 7, 2, 5, 9 };

        /// <summary>
        /// Day offsets, by descending rank, applied to <see cref="BaseDate"/>. Ranks 4 and 5 share a value so the
        /// data contains a deliberate tie on the sort value.
        /// </summary>
        private static readonly int[] SortedSetDayOffsetByRank = { 90, 80, 70, 60, 50, 50, 40, 30, 20, 10 };

        /// <summary>
        /// Creation indexes of the "phase" data set resources that have a date, ordered by date descending.
        /// The remaining 8 creation indexes (0, 3, 4, 6, 9, 10, 12 and 14) have no date at all, which forces the
        /// SQL search to run in two phases: the resources that have the sort value, and the resources that do not.
        /// <para>
        /// The dated and undated resources are deliberately interleaved in creation (surrogate ID) order, and the
        /// date order is non-monotonic, so that inside each phase a page of matches is still not a contiguous
        /// surrogate ID range. With a page size of 3 and a descending sort the first phase pages are {8,1,13} and
        /// {5,11,2}: the endpoints of {8,1,13} are 8 and 13, which excludes match 1 and spans non-matches 9 to 12.
        /// </para>
        /// <para>
        /// 7 dated and 8 undated resources means that, with a page size of 3, both a descending sort (dated first)
        /// and an ascending sort (undated first) produce at least two whole pages inside each phase plus a page at
        /// the phase boundary.
        /// </para>
        /// </summary>
        private static readonly int[] PhaseSetDescendingRankToCreationIndex = { 8, 1, 13, 5, 11, 2, 7 };

        /// <summary>
        /// Day offsets, by descending rank, of the dated resources of the "phase" data set. Ranks 3 and 4 share a
        /// value so this data set also contains a deliberate tie on the sort value.
        /// </summary>
        private static readonly int[] PhaseSetDayOffsetByRank = { 95, 85, 75, 65, 65, 55, 45 };

        private static readonly DateTimeOffset BaseDate = new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly Dictionary<string, int?> _sortDayOffsetByReference = new Dictionary<string, int?>(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<string>> _includedReferencesByReport = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<string>> _reportReferencesByObservation = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        private readonly List<string> _sortedSetReportReferences = new List<string>();
        private readonly List<string> _sortedSetObservationReferences = new List<string>();
        private readonly List<string> _phaseSetReportReferences = new List<string>();
        private readonly string _sortedTag;
        private readonly string _phaseTag;

        public SortedIncludesTestFixture(
            DataStore dataStore,
            Format format,
            TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            var runId = Guid.NewGuid().ToString("N");
            _sortedTag = $"sortedincludes-sorted-{runId}";
            _phaseTag = $"sortedincludes-phase-{runId}";
        }

        /// <summary>
        /// Gets the tag identifying the "sorted" data set, where every DiagnosticReport has a date.
        /// </summary>
        public string SortedTag => _sortedTag;

        /// <summary>
        /// Gets the tag identifying the "phase" data set, where only some DiagnosticReports have a date.
        /// </summary>
        public string PhaseTag => _phaseTag;

        /// <summary>
        /// Gets the references ("DiagnosticReport/{id}") of the "sorted" data set, in creation order.
        /// </summary>
        public IReadOnlyList<string> SortedSetReportReferences => new ReadOnlyCollection<string>(_sortedSetReportReferences);

        /// <summary>
        /// Gets the references ("Observation/{id}") of the "sorted" data set, in creation order.
        /// </summary>
        public IReadOnlyList<string> SortedSetObservationReferences => new ReadOnlyCollection<string>(_sortedSetObservationReferences);

        /// <summary>
        /// Gets the references ("DiagnosticReport/{id}") of the "phase" data set, in creation order.
        /// </summary>
        public IReadOnlyList<string> PhaseSetReportReferences => new ReadOnlyCollection<string>(_phaseSetReportReferences);

        /// <summary>
        /// Gets the Observation and Encounter references that <c>_include=DiagnosticReport:result</c> and
        /// <c>_include=DiagnosticReport:encounter</c> must return for the given DiagnosticReport.
        /// </summary>
        /// <param name="reportReference">A "DiagnosticReport/{id}" reference.</param>
        /// <returns>The expected included references.</returns>
        public IReadOnlyList<string> IncludedReferencesForReport(string reportReference)
        {
            return _includedReferencesByReport.TryGetValue(reportReference, out var references)
                ? references
                : throw new InvalidOperationException($"'{reportReference}' is not part of the test data.");
        }

        /// <summary>
        /// Gets the DiagnosticReport references that <c>_revinclude=DiagnosticReport:result</c> must return for
        /// the given Observation.
        /// </summary>
        /// <param name="observationReference">An "Observation/{id}" reference.</param>
        /// <returns>The expected included references.</returns>
        public IReadOnlyList<string> ReverseIncludedReferencesForObservation(string observationReference)
        {
            return _reportReferencesByObservation.TryGetValue(observationReference, out var references)
                ? references
                : throw new InvalidOperationException($"'{observationReference}' is not part of the test data.");
        }

        /// <summary>
        /// Gets the day offset used to build the date of a resource, or null when the resource has no date.
        /// Used to assert that the server actually honored the requested sort.
        /// </summary>
        /// <param name="reference">A "{resourceType}/{id}" reference.</param>
        /// <returns>The day offset, or null when the resource has no date.</returns>
        public int? SortDayOffsetFor(string reference)
        {
            return _sortDayOffsetByReference.TryGetValue(reference, out var dayOffset)
                ? dayOffset
                : throw new InvalidOperationException($"'{reference}' is not part of the test data.");
        }

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
            await InitializeSortedSetAsync();
            await InitializePhaseSetAsync();
        }

        private async Task InitializeSortedSetAsync()
        {
            var sortedSetDayOffsets = new int?[SortedSetSize];
            for (var rank = 0; rank < SortedSetDescendingRankToCreationIndex.Length; rank++)
            {
                sortedSetDayOffsets[SortedSetDescendingRankToCreationIndex[rank]] = SortedSetDayOffsetByRank[rank];
            }

            for (var creationIndex = 0; creationIndex < SortedSetSize; creationIndex++)
            {
                var (reportReference, observationReference) = await CreateTripleAsync(_sortedTag, creationIndex, sortedSetDayOffsets[creationIndex], sortedSetDayOffsets[creationIndex]);
                _sortedSetReportReferences.Add(reportReference);
                _sortedSetObservationReferences.Add(observationReference);
            }
        }

        private async Task InitializePhaseSetAsync()
        {
            var phaseSetDayOffsets = new int?[PhaseSetSize];
            for (var rank = 0; rank < PhaseSetDescendingRankToCreationIndex.Length; rank++)
            {
                phaseSetDayOffsets[PhaseSetDescendingRankToCreationIndex[rank]] = PhaseSetDayOffsetByRank[rank];
            }

            for (var creationIndex = 0; creationIndex < PhaseSetSize; creationIndex++)
            {
                var (reportReference, _) = await CreateTripleAsync(_phaseTag, creationIndex, phaseSetDayOffsets[creationIndex], observationDayOffset: null);
                _phaseSetReportReferences.Add(reportReference);
            }
        }

        private static Meta CreateMeta(string tag)
        {
            return new Meta
            {
                Tag = new List<Coding> { new Coding(TagSystem, tag) },
            };
        }

        private static CodeableConcept CreateCode(string tag, int creationIndex)
        {
            return new CodeableConcept(CodeSystem, $"{tag}-{creationIndex}");
        }

        private async System.Threading.Tasks.Task<(string ReportReference, string ObservationReference)> CreateTripleAsync(
            string tag,
            int creationIndex,
            int? reportDayOffset,
            int? observationDayOffset)
        {
            // The resources are created one at a time, and never in a batch, so that creation order matches
            // ascending resource surrogate ID order in SQL.
            var observation = new Observation
            {
                Meta = CreateMeta(tag),
                Status = ObservationStatus.Final,
                Code = CreateCode(tag, creationIndex),
            };

            if (observationDayOffset.HasValue)
            {
                observation.Effective = new FhirDateTime(BaseDate.AddDays(observationDayOffset.Value));
            }

            var createdObservation = await TestFhirClient.CreateAsync(observation);

            var encounter = new Encounter
            {
                Meta = CreateMeta(tag),
#if Stu3 || R4 || R4B
                Status = Encounter.EncounterStatus.Finished,
                Class = new Coding(EncounterClassSystem, "AMB"),
#else
                Status = EncounterStatus.Completed,
                Class = new List<CodeableConcept> { new CodeableConcept(EncounterClassSystem, "AMB") },
#endif
            };

            var createdEncounter = await TestFhirClient.CreateAsync(encounter);

            var observationReference = $"{KnownResourceTypes.Observation}/{createdObservation.Resource.Id}";
            var encounterReference = $"{KnownResourceTypes.Encounter}/{createdEncounter.Resource.Id}";

            var report = new DiagnosticReport
            {
                Meta = CreateMeta(tag),
                Status = DiagnosticReport.DiagnosticReportStatus.Final,
                Code = CreateCode(tag, creationIndex),
                Result = new List<ResourceReference> { new ResourceReference(observationReference) },
            };

#if Stu3
            // STU3 models the encounter of a DiagnosticReport as 'context'.
            report.Context = new ResourceReference(encounterReference);
#else
            report.Encounter = new ResourceReference(encounterReference);
#endif

            if (reportDayOffset.HasValue)
            {
                report.Effective = new FhirDateTime(BaseDate.AddDays(reportDayOffset.Value));
            }

            var createdReport = await TestFhirClient.CreateAsync(report);
            var reportReference = $"{KnownResourceTypes.DiagnosticReport}/{createdReport.Resource.Id}";

            _includedReferencesByReport[reportReference] = new ReadOnlyCollection<string>(
                new List<string> { observationReference, encounterReference });
            _reportReferencesByObservation[observationReference] = new ReadOnlyCollection<string>(
                new List<string> { reportReference });
            _sortDayOffsetByReference[reportReference] = reportDayOffset;
            _sortDayOffsetByReference[observationReference] = observationDayOffset;

            return (reportReference, observationReference);
        }
    }
}
