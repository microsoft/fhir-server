// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class OverlongReferenceIdCountTests
    {
        private static readonly SearchParameterInfo Performer = new SearchParameterInfo(
            "performer", "performer", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-performer"));

        private static readonly SearchParameterInfo Subject = new SearchParameterInfo(
            "subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

        [Fact]
        public void GivenResourcesWithOverlongReferences_WhenCounted_ThenEachResourceIsCountedOncePerSearchParameter()
        {
            var counts = new List<OverlongReferenceIdCount>();

            OverlongReferenceIdCount.Add(counts, Wrap("obs-1", new SearchIndexEntry(Performer, Overlong(65)), new SearchIndexEntry(Performer, Overlong(70)), new SearchIndexEntry(Subject, Overlong(80))));
            OverlongReferenceIdCount.Add(counts, Wrap("obs-2", new SearchIndexEntry(Performer, Overlong(65))));
            OverlongReferenceIdCount.Add(counts, Wrap("obs-3", new SearchIndexEntry(Performer, Valid())));

            Assert.Collection(
                counts.OrderBy(c => c.SearchParameterCode, StringComparer.Ordinal),
                c => AssertCount(c, "Observation", "performer", 64, 2),
                c => AssertCount(c, "Observation", "subject", 64, 1));
        }

        [Fact]
        public void GivenAReferenceTokenCompositeWithAnOverlongReference_WhenCounted_ThenItIsCounted()
        {
            var counts = new List<OverlongReferenceIdCount>();
            var composite = new CompositeSearchValue(new[]
            {
                new ISearchValue[] { Overlong(65) },
                new ISearchValue[] { new TokenSearchValue(system: null, code: "code", text: null) },
            });

            OverlongReferenceIdCount.Add(counts, Wrap("obs-1", new SearchIndexEntry(Performer, composite)));

            AssertCount(Assert.Single(counts), "Observation", "performer", 64, 1);
        }

        [Fact]
        public void GivenAHistoryOrNullResource_WhenCounted_ThenNothingIsCounted()
        {
            var counts = new List<OverlongReferenceIdCount>();
            ResourceWrapper history = Wrap("obs-1", new SearchIndexEntry(Performer, Overlong(65)));
            history.IsHistory = true;

            OverlongReferenceIdCount.Add(counts, history);
            OverlongReferenceIdCount.Add(counts, null);

            Assert.Empty(counts);
        }

        [Fact]
        public void GivenCountsFromSeveralJobs_WhenMerged_ThenMatchingCountsAreSummed()
        {
            var target = new List<OverlongReferenceIdCount> { new OverlongReferenceIdCount("Observation", "performer", 64, 2) };

            OverlongReferenceIdCount.Merge(target, new[]
            {
                new OverlongReferenceIdCount("Observation", "performer", 64, 3),
                new OverlongReferenceIdCount("Patient", "general-practitioner", 64, 1),
            });
            OverlongReferenceIdCount.Merge(target, null);

            Assert.Collection(
                target,
                c => AssertCount(c, "Observation", "performer", 64, 5),
                c => AssertCount(c, "Patient", "general-practitioner", 64, 1));
        }

        [Fact]
        public void GivenJobResultsWithoutCounts_WhenSerialized_ThenTheFieldIsOmitted()
        {
            Assert.DoesNotContain(JobRecordProperties.OverlongReferenceIds, JsonConvert.SerializeObject(new ReindexProcessingJobResult()), StringComparison.Ordinal);
            Assert.DoesNotContain(JobRecordProperties.OverlongReferenceIds, JsonConvert.SerializeObject(new ImportProcessingJobResult()), StringComparison.Ordinal);
            Assert.DoesNotContain(JobRecordProperties.OverlongReferenceIds, JsonConvert.SerializeObject(new BulkUpdateResult()), StringComparison.Ordinal);
        }

        [Fact]
        public void GivenJobResultsWithCounts_WhenRoundTripped_ThenTheCountsArePreserved()
        {
            var reindex = new ReindexProcessingJobResult();
            reindex.OverlongReferenceIds.Add(new OverlongReferenceIdCount("Observation", "performer", 64, 2));
            var import = new ImportProcessingJobResult();
            import.OverlongReferenceIds.Add(new OverlongReferenceIdCount("Observation", "performer", 128, 3));
            var bulkUpdate = new BulkUpdateResult();
            bulkUpdate.OverlongReferenceIds.Add(new OverlongReferenceIdCount("Patient", "general-practitioner", 64, 4));

            AssertCount(Assert.Single(JsonConvert.DeserializeObject<ReindexProcessingJobResult>(JsonConvert.SerializeObject(reindex)).OverlongReferenceIds), "Observation", "performer", 64, 2);
            AssertCount(Assert.Single(JsonConvert.DeserializeObject<ImportProcessingJobResult>(JsonConvert.SerializeObject(import)).OverlongReferenceIds), "Observation", "performer", 128, 3);
            AssertCount(Assert.Single(JsonConvert.DeserializeObject<BulkUpdateResult>(JsonConvert.SerializeObject(bulkUpdate)).OverlongReferenceIds), "Patient", "general-practitioner", 64, 4);
        }

        [Fact]
        public void GivenJobResultsFromAnOlderVersion_WhenDeserialized_ThenThereAreNoCounts()
        {
            Assert.Empty(JsonConvert.DeserializeObject<ReindexProcessingJobResult>("{\"SucceededResourceCount\":5}").OverlongReferenceIds);
            Assert.Empty(JsonConvert.DeserializeObject<ImportProcessingJobResult>("{\"SucceededResources\":5}").OverlongReferenceIds);
            Assert.Empty(JsonConvert.DeserializeObject<BulkUpdateResult>("{\"resourcesUpdated\":{\"Patient\":5}}").OverlongReferenceIds);
        }

        [Fact]
        public void GivenOverlongReferences_WhenFound_ThenOnlyFlaggedReferencesAreReturned()
        {
            var entries = new[]
            {
                new SearchIndexEntry(Performer, Overlong(65)),
                new SearchIndexEntry(Performer, Valid()),
                new SearchIndexEntry(Subject, new StringSearchValue("not a reference")),
            };

            (SearchIndexEntry entry, ReferenceSearchValue reference) = Assert.Single(LongReferenceIdIssue.GetOverlongReferences(entries));
            Assert.Equal("performer", entry.SearchParameter.Code);
            Assert.Equal(65, reference.OverlongResourceIdLength);
            Assert.Empty(LongReferenceIdIssue.GetOverlongReferences(null));
        }

        private static void AssertCount(OverlongReferenceIdCount count, string resourceType, string code, int maxLength, long expected)
        {
            Assert.Equal(resourceType, count.ResourceType);
            Assert.Equal(code, count.SearchParameterCode);
            Assert.Equal(maxLength, count.MaxResourceIdLength);
            Assert.Equal(expected, count.Count);
        }

        private static ReferenceSearchValue Overlong(int length)
            => new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null, "Patient", new string('a', 64)) { OverlongResourceIdLength = length, MaxResourceIdLength = 64 };

        private static ReferenceSearchValue Valid()
            => new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null, "Patient", new string('a', 64));

        private static ResourceWrapper Wrap(string id, params SearchIndexEntry[] entries)
            => new ResourceWrapper(id, "1", "Observation", null, null, DateTimeOffset.UnixEpoch, false, entries, null, null);
    }
}
