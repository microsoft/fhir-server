// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Caching;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Caching
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Caching)]
    public class ScopedCacheTests
    {
        private readonly TestPartitionCache<string> _cache = new((partitionId, _, _) => new ValueTask<string>(partitionId ?? "default"));
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

        [Fact]
        public async Task GivenARequestForAPartition_WhenRead_ThenThatPartitionsValueIsReturned()
        {
            IPartitionInfo partition = Substitute.For<IPartitionInfo>();
            partition.Id.Returns("b");
            _requestContextAccessor.RequestContext.Partition.Returns(partition);
            var scopedCache = new ScopedCache<string>(_cache, _requestContextAccessor);

            Assert.Equal("b", await scopedCache.GetAsync());
            Assert.Equal("b", scopedCache.Get());
        }

        [Fact]
        public async Task GivenARequestWithoutAPartition_WhenRead_ThenTheUnpartitionedValueIsReturned()
        {
            _requestContextAccessor.RequestContext.Partition.ReturnsNull();
            var scopedCache = new ScopedCache<string>(_cache, _requestContextAccessor);

            Assert.Equal("default", await scopedCache.GetAsync());
        }
    }
}
