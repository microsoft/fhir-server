// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ParserOptionsTests
    {
        [Fact]
        public void GivenColumnValue_WhenAddParameter_ThenAddsTypedCommandParameter()
        {
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            object result = options.AddParameter(VLatest.StringSearchParam.Text, "Smith%", includeInHash: true);

            Assert.Equal("@p0", result.ToString());
            Assert.Equal("Smith%", command.Parameters["@p0"].Value);
            Assert.Equal(SqlDbType.NVarChar, command.Parameters["@p0"].SqlDbType);
        }

        [Fact]
        public void GivenNoManager_WhenAddParameter_ThenThrows()
        {
            var options = new ParserOptions();

            Assert.Throws<InvalidOperationException>(
                () => options.AddParameter(VLatest.StringSearchParam.Text, "Smith", includeInHash: true));
        }
    }
}
