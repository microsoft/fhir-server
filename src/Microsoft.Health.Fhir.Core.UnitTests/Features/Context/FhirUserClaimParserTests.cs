// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Context
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public class FhirUserClaimParserTests
    {
        [Theory]
        [MemberData(nameof(GetFhirUserClaims))]
        public void GivenAValidFhirUserClaim_WhenParsed_ThenResourceTypeAndIdStoredInContext(Uri fhirUserClaimUri, string expectedResourceType, string expectedId)
        {
            var fhirRequestContext = Substitute.For<IFhirRequestContext>();
            var accessControlContext = new AccessControlContext();
            fhirRequestContext.AccessControlContext.Returns(accessControlContext);
            fhirRequestContext.AccessControlContext.FhirUserClaim = fhirUserClaimUri;

            FhirUserClaimParser.ParseFhirUserClaim(fhirRequestContext.AccessControlContext, true);

            Assert.Equal(expectedResourceType, fhirRequestContext.AccessControlContext.CompartmentResourceType);
            Assert.Equal(expectedId, fhirRequestContext.AccessControlContext.CompartmentId);
        }

        [Theory]
        [MemberData(nameof(GetInvalidFhirUserClaims))]
        public void GivenAnInvalidFhirUserClaim_WhenParsed_ThenExceptionIsThrown(Uri fhirUserClaimUri)
        {
            var fhirRequestContext = Substitute.For<IFhirRequestContext>();
            var accessControlContext = new AccessControlContext();
            fhirRequestContext.AccessControlContext.Returns(accessControlContext);
            fhirRequestContext.AccessControlContext.FhirUserClaim = fhirUserClaimUri;

            Assert.Throws<BadRequestException>(() => FhirUserClaimParser.ParseFhirUserClaim(fhirRequestContext.AccessControlContext, true));
        }

        public static IEnumerable<object[]> GetFhirUserClaims()
        {
            yield return new object[] { new Uri("Patient/id1", UriKind.RelativeOrAbsolute), "Patient", "id1" };
            yield return new object[] { new Uri("https://fhirserver/Patient/id1", UriKind.RelativeOrAbsolute), "Patient", "id1" };
            yield return new object[] { new Uri("https://fhirserver/Practitioner/foo1", UriKind.RelativeOrAbsolute), "Practitioner", "foo1" };
            yield return new object[] { new Uri("Practitioner/foo1/", UriKind.RelativeOrAbsolute), "Practitioner", "foo1" };
        }

        public static IEnumerable<object[]> GetInvalidFhirUserClaims()
        {
            yield return new object[] { new Uri("Patient/", UriKind.RelativeOrAbsolute) };
            yield return new object[] { new Uri("https://fhirserver/Observation/id1") };
            yield return new object[] { new Uri("Foo", UriKind.RelativeOrAbsolute) };
        }
    }
}
