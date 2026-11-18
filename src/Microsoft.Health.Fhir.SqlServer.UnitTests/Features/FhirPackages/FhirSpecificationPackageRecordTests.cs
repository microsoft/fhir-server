// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.FhirPackages;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.FhirPackages
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class FhirSpecificationPackageRecordTests
    {
        private const string PackageId = "partner.search";
        private const string Version = "1.2.3";
        private const string Integrity = "sha512-abc";

        [Theory]
        [InlineData(PackageId, Version, Integrity, true)]
        [InlineData("partner.other", Version, Integrity, false)]
        [InlineData(PackageId, "9.9.9", Integrity, false)]
        [InlineData(PackageId, Version, "sha512-other", false)]
        [InlineData(PackageId, Version, "SHA512-ABC", false)]
        public void GivenTwoRecords_WhenCompared_ThenOnlyTheSameIdVersionAndIntegrityMatch(string packageId, string version, string integrity, bool expected)
        {
            // Arrange
            var installed = new FhirSpecificationPackageRecord(PackageId, Version, Integrity, new[] { "http://partner.example/SearchParameter/one" });
            var candidate = new FhirSpecificationPackageRecord(packageId, version, integrity, new[] { "http://partner.example/SearchParameter/one" });

            // Act
            bool actual = installed.IsSameContentAs(candidate);

            // Assert
            Assert.Equal(expected, actual);
        }
    }
}
