// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ChainSearchGroupTests
    {
        [Fact]
        public void GivenEmptyDictionary_WhenGroupChainedParameters_ThenReturnsEmptyList()
        {
            var input = new Dictionary<string, IList<string>>();
            var result = ChainSearchGroup.GroupChainedParameters(input);
            Assert.Empty(result);
        }

        [Fact]
        public void GivenSingleChain_WhenGroupChainedParameters_ThenReturnsSingleGroup()
        {
            var input = new Dictionary<string, IList<string>>
            {
                { "subject.name", new List<string> { "John" } },
            };

            var result = ChainSearchGroup.GroupChainedParameters(input);
            Assert.Single(result);
            Assert.Equal("subject", result[0].GroupKey);
            Assert.False(result[0].IsReverseChain);
            Assert.Single(result[0].Entries);
            Assert.Equal("name", result[0].Entries[0].RemainingChain);
            Assert.Equal("John", result[0].Entries[0].Value);
        }

        [Fact]
        public void GivenMultipleChainsWithSameRef_WhenGroupChainedParameters_ThenGroupsTogether()
        {
            var input = new Dictionary<string, IList<string>>
            {
                { "subject.name", new List<string> { "John" } },
                { "subject.birthdate", new List<string> { "2000-01-01" } },
            };

            var result = ChainSearchGroup.GroupChainedParameters(input);
            Assert.Single(result);
            Assert.Equal(2, result[0].Entries.Count);
        }

        [Fact]
        public void GivenChainsWithDifferentRefs_WhenGroupChainedParameters_ThenCreatesSeparateGroups()
        {
            var input = new Dictionary<string, IList<string>>
            {
                { "subject.name", new List<string> { "John" } },
                { "performer.name", new List<string> { "Dr Smith" } },
            };

            var result = ChainSearchGroup.GroupChainedParameters(input);
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void GivenEntryWithoutDot_WhenGroupChainedParameters_ThenSkipsIt()
        {
            var input = new Dictionary<string, IList<string>>
            {
                { "nodot", new List<string> { "value" } },
            };

            var result = ChainSearchGroup.GroupChainedParameters(input);
            Assert.Empty(result);
        }

        [Fact]
        public void GivenMultipleValuesForSameParam_WhenGroupChainedParameters_ThenCreatesEntryPerValue()
        {
            var input = new Dictionary<string, IList<string>>
            {
                { "subject.name", new List<string> { "John", "Jane" } },
            };

            var result = ChainSearchGroup.GroupChainedParameters(input);
            Assert.Single(result);
            Assert.Equal(2, result[0].Entries.Count);
            Assert.Equal("John", result[0].Entries[0].Value);
            Assert.Equal("Jane", result[0].Entries[1].Value);
        }

        [Fact]
        public void GivenTypedRefChain_WhenGroupChainedParameters_ThenPreservesTypeInGroupKey()
        {
            var input = new Dictionary<string, IList<string>>
            {
                { "subject:Patient.name", new List<string> { "John" } },
            };

            var result = ChainSearchGroup.GroupChainedParameters(input);
            Assert.Single(result);
            Assert.Equal("subject:Patient", result[0].GroupKey);
            Assert.Equal("subject:Patient", result[0].Entries[0].ReferenceParamCode);
        }

        // Reverse chain grouping tests

        [Fact]
        public void GivenEmptyDictionary_WhenGroupReversedChainedParameters_ThenReturnsEmptyList()
        {
            var input = new Dictionary<string, IList<string>>();
            var result = ChainSearchGroup.GroupReversedChainedParameters(input);
            Assert.Empty(result);
        }

        [Fact]
        public void GivenSingleReverseChain_WhenGroupReversedChainedParameters_ThenReturnsSingleGroup()
        {
            var input = new Dictionary<string, IList<string>>
            {
                { "_has:Coverage:beneficiary:identifier", new List<string> { "12345" } },
            };

            var result = ChainSearchGroup.GroupReversedChainedParameters(input);
            Assert.Single(result);
            Assert.Equal("Coverage:beneficiary", result[0].GroupKey);
            Assert.True(result[0].IsReverseChain);
            Assert.Single(result[0].Entries);
            Assert.Equal("identifier", result[0].Entries[0].RemainingChain);
            Assert.Equal("12345", result[0].Entries[0].Value);
            Assert.Equal("Coverage", result[0].Entries[0].SourceResourceType);
        }

        [Fact]
        public void GivenMultipleReverseChainsSameGroup_WhenGroupReversedChainedParameters_ThenGroupsTogether()
        {
            var input = new Dictionary<string, IList<string>>
            {
                { "_has:Coverage:beneficiary:identifier", new List<string> { "12345" } },
                { "_has:Coverage:beneficiary:status", new List<string> { "active" } },
            };

            var result = ChainSearchGroup.GroupReversedChainedParameters(input);
            Assert.Single(result);
            Assert.Equal(2, result[0].Entries.Count);
        }

        [Fact]
        public void GivenReverseChainsDifferentResourceTypes_WhenGroupReversedChainedParameters_ThenCreatesSeparateGroups()
        {
            var input = new Dictionary<string, IList<string>>
            {
                { "_has:Coverage:beneficiary:identifier", new List<string> { "12345" } },
                { "_has:Observation:subject:code", new List<string> { "vital" } },
            };

            var result = ChainSearchGroup.GroupReversedChainedParameters(input);
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void GivenEntryWithTooFewParts_WhenGroupReversedChainedParameters_ThenSkipsIt()
        {
            var input = new Dictionary<string, IList<string>>
            {
                { "_has:Coverage:beneficiary", new List<string> { "value" } },
            };

            var result = ChainSearchGroup.GroupReversedChainedParameters(input);
            Assert.Empty(result);
        }
    }
}
