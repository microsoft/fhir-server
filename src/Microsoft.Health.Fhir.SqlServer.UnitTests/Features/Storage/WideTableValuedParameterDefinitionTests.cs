// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    /// <summary>
    /// Regressions for the Wide* table-valued parameter definitions that replace the resource id column
    /// metadata when a larger maximum resource id length is configured. At the default length the emitted
    /// metadata must be identical to the generated definition; at an extended length only the resource id
    /// columns may change, and a full-length id must survive into the <see cref="SqlDataRecord"/>.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class WideTableValuedParameterDefinitionTests
    {
        private const string ParameterName = "@Rows";
        private const int DefaultMaxResourceIdLength = 64;
        private const int ExtendedMaxResourceIdLength = 128;

        [Theory]
        [InlineData(DefaultMaxResourceIdLength)]
        [InlineData(ExtendedMaxResourceIdLength)]
        public void GivenTheResourceListDefinition_WhenTheMaxResourceIdLengthChanges_ThenOnlyTheResourceIdColumnIsWidened(int maxResourceIdLength)
        {
            AssertResourceIdWidening<ResourceListRow>(
                new WideResourceListTableValuedParameterDefinition(ParameterName, maxResourceIdLength),
                new ResourceListTableValuedParameterDefinition(ParameterName),
                resourceId => new ResourceListRow(1, 2L, resourceId, 1, true, false, false, true, new MemoryStream(new byte[] { 1, 2, 3 }), false, "PUT", "hash"),
                "ResourceId",
                maxResourceIdLength);
        }

        [Theory]
        [InlineData(DefaultMaxResourceIdLength)]
        [InlineData(ExtendedMaxResourceIdLength)]
        public void GivenTheReferenceSearchParamListDefinition_WhenTheMaxResourceIdLengthChanges_ThenOnlyTheResourceIdColumnIsWidened(int maxResourceIdLength)
        {
            AssertResourceIdWidening<ReferenceSearchParamListRow>(
                new WideReferenceSearchParamListTableValuedParameterDefinition(ParameterName, maxResourceIdLength),
                new ReferenceSearchParamListTableValuedParameterDefinition(ParameterName),
                resourceId => new ReferenceSearchParamListRow(1, 2L, 3, null, null, resourceId, null),
                "ReferenceResourceId",
                maxResourceIdLength);
        }

        [Theory]
        [InlineData(DefaultMaxResourceIdLength)]
        [InlineData(ExtendedMaxResourceIdLength)]
        public void GivenTheReferenceTokenCompositeSearchParamListDefinition_WhenTheMaxResourceIdLengthChanges_ThenOnlyTheResourceIdColumnIsWidened(int maxResourceIdLength)
        {
            AssertResourceIdWidening<ReferenceTokenCompositeSearchParamListRow>(
                new WideReferenceTokenCompositeSearchParamListTableValuedParameterDefinition(ParameterName, maxResourceIdLength),
                new ReferenceTokenCompositeSearchParamListTableValuedParameterDefinition(ParameterName),
                resourceId => new ReferenceTokenCompositeSearchParamListRow(1, 2L, 3, null, null, resourceId, null, null, "code", null),
                "ReferenceResourceId1",
                maxResourceIdLength);
        }

        [Theory]
        [InlineData(DefaultMaxResourceIdLength)]
        [InlineData(ExtendedMaxResourceIdLength)]
        public void GivenTheResourceKeyListDefinition_WhenTheMaxResourceIdLengthChanges_ThenOnlyTheResourceIdColumnIsWidened(int maxResourceIdLength)
        {
            AssertResourceIdWidening<ResourceKeyListRow>(
                new WideResourceKeyListTableValuedParameterDefinition(ParameterName, maxResourceIdLength),
                new ResourceKeyListTableValuedParameterDefinition(ParameterName),
                resourceId => new ResourceKeyListRow(1, resourceId, 1),
                "ResourceId",
                maxResourceIdLength);
        }

        [Theory]
        [InlineData(DefaultMaxResourceIdLength)]
        [InlineData(ExtendedMaxResourceIdLength)]
        public void GivenTheResourceDateKeyListDefinition_WhenTheMaxResourceIdLengthChanges_ThenOnlyTheResourceIdColumnIsWidened(int maxResourceIdLength)
        {
            AssertResourceIdWidening<ResourceDateKeyListRow>(
                new WideResourceDateKeyListTableValuedParameterDefinition(ParameterName, maxResourceIdLength),
                new ResourceDateKeyListTableValuedParameterDefinition(ParameterName),
                resourceId => new ResourceDateKeyListRow(1, resourceId, 2L),
                "ResourceId",
                maxResourceIdLength);
        }

        private static void AssertResourceIdWidening<TRow>(
            TableValuedParameterDefinition<TRow> wideDefinition,
            TableValuedParameterDefinition<TRow> generatedDefinition,
            Func<string, TRow> rowFactory,
            string resourceIdColumnName,
            int maxResourceIdLength)
            where TRow : struct
        {
            // Arrange
            string resourceId = new string('a', maxResourceIdLength);

            // Act
            SqlDataRecord generatedRecord = GetSingleRecord(generatedDefinition, rowFactory(new string('a', DefaultMaxResourceIdLength)));
            SqlDataRecord actualRecord = GetSingleRecord(wideDefinition, rowFactory(resourceId));

            // Assert
            Assert.Equal(generatedRecord.FieldCount, actualRecord.FieldCount);

            for (int i = 0; i < generatedRecord.FieldCount; i++)
            {
                SqlMetaData expected = generatedRecord.GetSqlMetaData(i);
                SqlMetaData actual = actualRecord.GetSqlMetaData(i);

                Assert.Equal(expected.Name, actual.Name);
                Assert.Equal(expected.SqlDbType, actual.SqlDbType);
                Assert.Equal(expected.Name == resourceIdColumnName ? maxResourceIdLength : expected.MaxLength, actual.MaxLength);
                Assert.Equal(expected.Precision, actual.Precision);
                Assert.Equal(expected.Scale, actual.Scale);
                Assert.Equal(expected.LocaleId, actual.LocaleId);
                Assert.Equal(expected.CompareOptions, actual.CompareOptions);
            }

            Assert.Equal(resourceId, actualRecord.GetString(actualRecord.GetOrdinal(resourceIdColumnName)));
        }

        private static SqlDataRecord GetSingleRecord<TRow>(TableValuedParameterDefinition<TRow> definition, TRow row)
            where TRow : struct
        {
            using var command = new SqlCommand();

            SqlParameter parameter = definition.AddParameter(command.Parameters, new[] { row });

            return ((IEnumerable<SqlDataRecord>)parameter.Value).Single();
        }
    }
}
