// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search;
using Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Collection(ModelInfoProviderSerialCollection.Name)]
    public class SqlReferenceStorageValidatorTests : IClassFixture<ModelInfoProviderFixture>
    {
        private const string BaseUriPrefix = "https://example.org/";

        /// <summary>
        /// Gets the schema version, reference id length, and expected capacity (or null when accepted).
        /// </summary>
        public static TheoryData<int?, int, int?> ResourceIdLengths => new TheoryData<int?, int, int?>
        {
            { (int)SchemaVersion.V117, 63, null },
            { (int)SchemaVersion.V117, 64, null },
            { (int)SchemaVersion.V117, 65, 64 },
            { null, 64, null },
            { null, 65, 64 },
            { SchemaVersionConstants.Max, 127, null },
            { SchemaVersionConstants.Max, 128, null },
            { SchemaVersionConstants.Max, 129, 128 },
        };

        [Theory]
        [MemberData(nameof(ResourceIdLengths))]
        public void GivenAReferenceIdNearTheActiveColumnCapacity_WhenValidated_ThenItIsAcceptedWholeOrRejected(int? currentSchemaVersion, int length, int? expectedCapacity)
        {
            var validator = CreateValidator(currentSchemaVersion);
            string resourceId = CreateText(length);

            Exception exception = Record.Exception(() => validator.EnsureRepresentable(ReferenceSearchValue.FromLiteral(resourceId)));

            AssertOutcome(exception, expectedCapacity, FieldName.ReferenceResourceId, resourceId);
        }

        [Theory]
        [InlineData(127, null)]
        [InlineData(128, null)]
        [InlineData(129, 128)]
        public void GivenABaseUriNearTheColumnCapacityAndAShortId_WhenValidated_ThenItIsAcceptedWholeOrRejected(int length, int? expectedCapacity)
        {
            var validator = CreateValidator(SchemaVersionConstants.Max);
            Uri baseUri = CreateBaseUri(length);
            Assert.Equal(length, baseUri.ToString().Length);

            Exception exception = Record.Exception(() => validator.EnsureRepresentable(TestReferenceSearchValues.Resource(ReferenceKind.External, baseUri, "Patient", "p1")));

            AssertOutcome(exception, expectedCapacity, FieldName.ReferenceBaseUri, baseUri.ToString());
        }

        [Fact]
        public void GivenTwoBaseUrisSharingTheFirst128Characters_WhenValidated_ThenOnlyTheOneThatFitsIsAccepted()
        {
            var validator = CreateValidator(SchemaVersionConstants.Max);
            Uri fitting = CreateBaseUri(128);
            Uri longer = new Uri(fitting.ToString() + "extra/");
            Assert.Equal(134, longer.ToString().Length);
            Assert.StartsWith(fitting.ToString(), longer.ToString(), StringComparison.Ordinal);

            validator.EnsureRepresentable(TestReferenceSearchValues.Resource(ReferenceKind.External, fitting, "Patient", "p1"));
            var exception = Assert.Throws<ReferenceStorageCapacityException>(
                () => validator.EnsureRepresentable(TestReferenceSearchValues.Resource(ReferenceKind.External, longer, "Patient", "p1")));

            Assert.Equal(FieldName.ReferenceBaseUri, exception.Component);
            Assert.Equal(128, exception.Capacity);
        }

        [Fact]
        public void GivenAReferenceWithoutABaseUri_WhenValidated_ThenItIsAccepted()
        {
            var validator = CreateValidator(SchemaVersionConstants.Max);

            validator.EnsureRepresentable(TestReferenceSearchValues.Resource(ReferenceKind.Internal, null, "Patient", CreateText(128)));
        }

        [Fact]
        public void GivenAnOverlongLiteralReference_WhenValidated_ThenItIsRejectedRatherThanSliced()
        {
            var validator = CreateValidator(SchemaVersionConstants.Max);
            string literal = "urn:uuid:" + CreateText(200);

            var exception = Assert.Throws<ReferenceStorageCapacityException>(() => validator.EnsureRepresentable(ReferenceSearchValue.FromLiteral(literal)));

            Assert.Equal(FieldName.ReferenceResourceId, exception.Component);
            Assert.DoesNotContain(literal[..20], exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenALiteralWithACharacterOutsideTheColumnCodePage_WhenValidated_ThenItIsRejected()
        {
            var validator = CreateValidator(SchemaVersionConstants.Max);

            var exception = Assert.Throws<ReferenceStorageCapacityException>(() => validator.EnsureRepresentable(ReferenceSearchValue.FromLiteral("note-\u4f8b")));

            Assert.Equal(FieldName.ReferenceResourceId, exception.Component);
            Assert.Equal(128, exception.Capacity);
        }

        [Fact]
        public void GivenABaseUriWithACharacterOutsideTheColumnCodePage_WhenValidated_ThenItIsRejected()
        {
            var validator = CreateValidator(SchemaVersionConstants.Max);
            var baseUri = new Uri("https://example.org/\u4f8b/fhir/");

            var exception = Assert.Throws<ReferenceStorageCapacityException>(
                () => validator.EnsureRepresentable(TestReferenceSearchValues.Resource(ReferenceKind.External, baseUri, "Patient", "p1")));

            Assert.Equal(FieldName.ReferenceBaseUri, exception.Component);
        }

        [Fact]
        public void GivenALiteralWithACharacterInsideTheColumnCodePage_WhenValidated_ThenItIsAccepted()
        {
            var validator = CreateValidator(SchemaVersionConstants.Max);

            validator.EnsureRepresentable(ReferenceSearchValue.FromLiteral("caf\u00e9"));
        }

        private static SqlReferenceStorageValidator CreateValidator(int? currentSchemaVersion)
            => new SqlReferenceStorageValidator(new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = currentSchemaVersion });

        private static void AssertOutcome(Exception exception, int? expectedCapacity, FieldName expectedComponent, string submittedValue)
        {
            if (expectedCapacity == null)
            {
                Assert.Null(exception);
                return;
            }

            var capacityException = Assert.IsType<ReferenceStorageCapacityException>(exception);
            Assert.Equal(expectedComponent, capacityException.Component);
            Assert.Equal(expectedCapacity.Value, capacityException.Capacity);
            Assert.DoesNotContain(submittedValue, capacityException.Message, StringComparison.Ordinal);
        }

        private static string CreateText(int length)
            => string.Concat(Enumerable.Range(0, length).Select(i => (char)('A' + (i % 26))));

        private static Uri CreateBaseUri(int length)
            => new Uri(BaseUriPrefix + new string('a', length - BaseUriPrefix.Length - 1) + "/");
    }
}
