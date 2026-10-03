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

        [Fact]
        public void GivenTheResourceListDefinition_WhenTheMaxResourceIdLengthChanges_ThenOnlyTheResourceIdColumnIsWidened()
        {
            AssertResourceIdWidening<ResourceListRow>(
                maxResourceIdLength => new WideResourceListTableValuedParameterDefinition(ParameterName, maxResourceIdLength),
                new ResourceListTableValuedParameterDefinition(ParameterName),
                resourceId => new ResourceListRow(1, 2L, resourceId, 1, true, false, false, true, new MemoryStream(new byte[] { 1, 2, 3 }), false, "PUT", "hash"),
                "ResourceId");
        }

        [Fact]
        public void GivenTheReferenceSearchParamListDefinition_WhenTheMaxResourceIdLengthChanges_ThenOnlyTheResourceIdColumnIsWidened()
        {
            AssertResourceIdWidening<ReferenceSearchParamListRow>(
                maxResourceIdLength => new WideReferenceSearchParamListTableValuedParameterDefinition(ParameterName, maxResourceIdLength),
                new ReferenceSearchParamListTableValuedParameterDefinition(ParameterName),
                resourceId => new ReferenceSearchParamListRow(1, 2L, 3, null, null, resourceId, null),
                "ReferenceResourceId");
        }

        [Fact]
        public void GivenTheReferenceTokenCompositeSearchParamListDefinition_WhenTheMaxResourceIdLengthChanges_ThenOnlyTheResourceIdColumnIsWidened()
        {
            AssertResourceIdWidening<ReferenceTokenCompositeSearchParamListRow>(
                maxResourceIdLength => new WideReferenceTokenCompositeSearchParamListTableValuedParameterDefinition(ParameterName, maxResourceIdLength),
                new ReferenceTokenCompositeSearchParamListTableValuedParameterDefinition(ParameterName),
                resourceId => new ReferenceTokenCompositeSearchParamListRow(1, 2L, 3, null, null, resourceId, null, null, "code", null),
                "ReferenceResourceId1");
        }

        [Fact]
        public void GivenTheResourceKeyListDefinition_WhenTheMaxResourceIdLengthChanges_ThenOnlyTheResourceIdColumnIsWidened()
        {
            AssertResourceIdWidening<ResourceKeyListRow>(
                maxResourceIdLength => new WideResourceKeyListTableValuedParameterDefinition(ParameterName, maxResourceIdLength),
                new ResourceKeyListTableValuedParameterDefinition(ParameterName),
                resourceId => new ResourceKeyListRow(1, resourceId, 1),
                "ResourceId");
        }

        [Fact]
        public void GivenTheResourceDateKeyListDefinition_WhenTheMaxResourceIdLengthChanges_ThenOnlyTheResourceIdColumnIsWidened()
        {
            AssertResourceIdWidening<ResourceDateKeyListRow>(
                maxResourceIdLength => new WideResourceDateKeyListTableValuedParameterDefinition(ParameterName, maxResourceIdLength),
                new ResourceDateKeyListTableValuedParameterDefinition(ParameterName),
                resourceId => new ResourceDateKeyListRow(1, resourceId, 2L),
                "ResourceId");
        }

        private static void AssertResourceIdWidening<TRow>(
            Func<int, TableValuedParameterDefinition<TRow>> wideDefinitionFactory,
            TableValuedParameterDefinition<TRow> generatedDefinition,
            Func<string, TRow> rowFactory,
            params string[] resourceIdColumnNames)
            where TRow : struct
        {
            // Arrange
            string defaultLengthId = new string('a', DefaultMaxResourceIdLength);
            string extendedLengthId = new string('b', ExtendedMaxResourceIdLength);

            // Act
            SqlDataRecord generatedRecord = GetSingleRecord(generatedDefinition, rowFactory(defaultLengthId));
            SqlDataRecord defaultRecord = GetSingleRecord(wideDefinitionFactory(DefaultMaxResourceIdLength), rowFactory(defaultLengthId));
            SqlDataRecord extendedRecord = GetSingleRecord(wideDefinitionFactory(ExtendedMaxResourceIdLength), rowFactory(extendedLengthId));

            // Assert - at the default length the table type is bit-for-bit the generated one.
            Assert.Equal(generatedRecord.FieldCount, defaultRecord.FieldCount);

            for (int i = 0; i < generatedRecord.FieldCount; i++)
            {
                SqlMetaData expected = generatedRecord.GetSqlMetaData(i);
                SqlMetaData actual = defaultRecord.GetSqlMetaData(i);

                Assert.Equal(expected.Name, actual.Name);
                Assert.Equal(expected.SqlDbType, actual.SqlDbType);
                Assert.Equal(expected.MaxLength, actual.MaxLength);
                Assert.Equal(expected.Precision, actual.Precision);
                Assert.Equal(expected.Scale, actual.Scale);
                Assert.Equal(expected.LocaleId, actual.LocaleId);
                Assert.Equal(expected.CompareOptions, actual.CompareOptions);
            }

            // Assert - at an extended length only the resource id columns change, and the full id is carried.
            Assert.Equal(generatedRecord.FieldCount, extendedRecord.FieldCount);

            var widenedColumns = new List<string>();

            for (int i = 0; i < generatedRecord.FieldCount; i++)
            {
                SqlMetaData expected = generatedRecord.GetSqlMetaData(i);
                SqlMetaData actual = extendedRecord.GetSqlMetaData(i);

                Assert.Equal(expected.Name, actual.Name);
                Assert.Equal(expected.SqlDbType, actual.SqlDbType);

                if (resourceIdColumnNames.Contains(actual.Name, StringComparer.Ordinal))
                {
                    Assert.Equal((long)ExtendedMaxResourceIdLength, actual.MaxLength);
                    Assert.Equal(extendedLengthId, extendedRecord.GetString(i));
                    widenedColumns.Add(actual.Name);
                }
                else
                {
                    Assert.Equal(expected.MaxLength, actual.MaxLength);
                }
            }

            Assert.Equal(resourceIdColumnNames.Length, widenedColumns.Count);

            foreach (string resourceIdColumnName in resourceIdColumnNames)
            {
                Assert.Contains(resourceIdColumnName, widenedColumns, StringComparer.Ordinal);
            }
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
