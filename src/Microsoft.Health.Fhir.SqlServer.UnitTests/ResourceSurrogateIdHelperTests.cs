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
            var baseDate = DateTimeOffset.MinValue;
            long baseId = baseDate.ToSurrogateId();

            Assert.Equal(baseDate, (baseId + 79999).ToLastUpdated());
            Assert.Equal(TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond), (baseId + 80000).ToLastUpdated() - baseDate);

            long maxBaseId = ResourceSurrogateIdHelper.MaxDateTime.ToSurrogateId();

            Assert.Equal(ResourceSurrogateIdHelper.MaxDateTime.UtcDateTime.TruncateToMillisecond(), maxBaseId.ToLastUpdated());
            Assert.Equal(ResourceSurrogateIdHelper.MaxDateTime.UtcDateTime.TruncateToMillisecond(), (maxBaseId + 79999).ToLastUpdated());
        }

        [Fact]
        public void GivenADateTimeLargerThanTheLargestThatCanBeRepresentedAsASurrogateId_WhenTurnedIntoASurrogateId_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DateTimeOffset.MaxValue.ToSurrogateId());
        }
    }
}
