// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class ResourceSurrogateIdHelperTests
    {
        [Fact]
        public void GivenADateTime_WhenRepresentedAsASurrogateId_HasTheExpectedRange()
        {
            var baseDate = DateTime.MinValue;
            long baseId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(baseDate);

            Assert.Equal(baseDate, ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(baseId + 79999));
            Assert.Equal(TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond), ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(baseId + 80000) - baseDate);

            long maxBaseId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(ResourceSurrogateIdHelper.MaxDateTime);

            Assert.Equal(ResourceSurrogateIdHelper.MaxDateTime.TruncateToMillisecond(), ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(maxBaseId));
            Assert.Equal(ResourceSurrogateIdHelper.MaxDateTime.TruncateToMillisecond(), ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(maxBaseId + 79999));
        }

        [Fact]
        public void GivenADateTimeLargerThanTheLargestThatCanBeRepresentedAsASurrogateId_WhenTurnedIntoASurrogateId_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.MaxValue));
        }
    }
}
