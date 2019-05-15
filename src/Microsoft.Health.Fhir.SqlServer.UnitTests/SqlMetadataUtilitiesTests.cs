// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.SqlServer.Server;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests
{
    public class SqlMetadataUtilitiesTests
    {
        [Fact]
        public void GivenASqlMetadataInstanceWithDefaultScaleAndPrecision_WhenGettingMinAndMaxValues_ReturnsCorrectValues()
        {
            var sqlMetaData = new SqlMetaData("foo", SqlDbType.Decimal);
            Assert.Equal(-999999999999999999M, SqlMetadataUtilities.GetMinValueForDecimalColumn(sqlMetaData));
            Assert.Equal(999999999999999999M, SqlMetadataUtilities.GetMaxValueForDecimalColumn(sqlMetaData));
        }

        [Fact]
        public void GivenASqlMetadataInstanceWithSpecifiedScaleAndPrecision_WhenGettingMinAndMaxValues_ReturnsCorrectValues()
        {
            var sqlMetaData = new SqlMetaData("foo", SqlDbType.Decimal, precision: 10, scale: 3);
            Assert.Equal(-9999999.999M, SqlMetadataUtilities.GetMinValueForDecimalColumn(sqlMetaData));
            Assert.Equal(9999999.999M, SqlMetadataUtilities.GetMaxValueForDecimalColumn(sqlMetaData));
        }
    }
}
