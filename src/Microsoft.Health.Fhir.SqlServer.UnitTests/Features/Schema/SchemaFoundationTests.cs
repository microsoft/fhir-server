// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Schema
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Schema)]
    public class SchemaFoundationTests
    {
        [Fact]
        public void GivenReleasedSchemaGenerator_WhenLatestModelIsBuilt_ThenVectorColumnMetadataIsAvailable()
        {
            VectorColumn embedding = VLatest.VectorSearchParam.Embedding;

            Assert.Equal("Embedding", embedding.ToString());
            Assert.Equal(1536, embedding.Dimensions);
        }

        [Theory]
        [InlineData((int)SchemaVersion.V117, false)]
        [InlineData((int)SchemaVersion.V119, true)]
        public void GivenVectorSchemaScript_WhenRead_ThenUnsupportedEnginesAreRejectedBeforeVectorDdl(
            int schemaVersion,
            bool applyFullSchemaSnapshot)
        {
            var scriptProvider = new ScriptProvider<SchemaVersion>();

            string script = scriptProvider.GetMigrationScript(schemaVersion, applyFullSchemaSnapshot);

            int guardIndex = script.IndexOf("ProductMajorVersion", StringComparison.Ordinal);
            int vectorTableIndex = script.IndexOf("VectorSearchParam", StringComparison.Ordinal);
            Assert.True(guardIndex >= 0, "The schema script must validate native vector support.");
            Assert.True(vectorTableIndex > guardIndex, "The native vector support guard must execute before vector DDL.");
        }
    }
}
