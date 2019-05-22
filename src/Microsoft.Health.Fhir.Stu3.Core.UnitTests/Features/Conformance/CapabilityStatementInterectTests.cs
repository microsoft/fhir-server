// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Tests.Common.Mocks;
using Xunit;
using static Hl7.Fhir.Model.CapabilityStatement;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    public class CapabilityStatementInterectTests
    {
        [Fact]
        public void GivenACapabilityStatement_WhenSupportingCreateConfiguringCreateAndDelete_ThenNotSupportedExceptionIsThrown()
        {
            var supported = GetMockedListedCapabilityStatement();
            supported.TryAddRestInteraction(ResourceType.Account, TypeRestfulInteraction.Create);

            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(configured, ResourceType.Account, new[] { TypeRestfulInteraction.Create, TypeRestfulInteraction.Delete });

            Assert.Throws<UnsupportedConfigurationException>(() => supported.Intersect(configured, strictConfig: true));
        }

        [Fact]
        public void GivenACapabilityStatement_WhenSupportingCreateConfiguringCreateAndDeleteWithNotStrictIntersect_ThenOnlyCreateIsReturned()
        {
            var supported = GetMockedListedCapabilityStatement();
            supported.TryAddRestInteraction(ResourceType.Account, TypeRestfulInteraction.Create);

            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(configured, ResourceType.Account, new[] { TypeRestfulInteraction.Create, TypeRestfulInteraction.Delete });

            var result = supported.Intersect(configured, strictConfig: false);
            Assert.NotNull(result);
            Assert.Single(result.Rest.Single().Resource);
            Assert.Single(result.Rest.Single().Resource.Single().Interaction);
            Assert.Equal(TypeRestfulInteraction.Create, result.Rest.Single().Resource.Single().Interaction.Single().Code);
        }

        [Fact]
        public void GivenACapabilityStatement_WhenSupportingCreateDeleteAndConfiguringCreate_ThenResultIsCorrectlyIntersected()
        {
            var supported = GetMockedListedCapabilityStatement();
            supported.TryAddRestInteraction(ResourceType.Account, TypeRestfulInteraction.Create);
            supported.TryAddRestInteraction(ResourceType.Account, TypeRestfulInteraction.Delete);

            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(configured, ResourceType.Account, new[] { TypeRestfulInteraction.Create });

            var result = supported.Intersect(configured, strictConfig: true);

            Assert.Single(result.Rest.Single().Resource);
            Assert.Single(result.Rest.Single().Resource.Single().Interaction);
            Assert.Equal(TypeRestfulInteraction.Create, result.Rest.Single().Resource.Single().Interaction.Single().Code);
        }

        [Fact]
        public void GivenACapabilityStatement_WhenSupportingAccountObservationAndConfiguringObservation_ThenResultIsCorrectlyIntersected()
        {
            var supported = GetMockedListedCapabilityStatement();
            supported.TryAddRestInteraction(ResourceType.Account, TypeRestfulInteraction.Create);
            supported.TryAddRestInteraction(ResourceType.Observation, TypeRestfulInteraction.Create);

            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(configured, ResourceType.Account, new[] { TypeRestfulInteraction.Create });

            var result = supported.Intersect(configured, strictConfig: true);

            Assert.Single(result.Rest.Single().Resource);
            Assert.Single(result.Rest.Single().Resource.Single().Interaction);
            Assert.Equal(TypeRestfulInteraction.Create, result.Rest.Single().Resource.Single().Interaction.Single().Code);
        }

        [Fact]
        public void GivenACapabilityStatement_WhenIntersectingResourceProfiles_ThenDefaultIsUsedWhenNoneSpecified()
        {
            var referenceUrl = "https://bing.com/";

            var supported = GetMockedListedCapabilityStatement();
            supported.TryAddRestInteraction(ResourceType.Observation, TypeRestfulInteraction.Create);
            supported.Rest.Single().Resource.Single().Profile = new ResourceReference(referenceUrl);

            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(configured, ResourceType.Observation, new[] { TypeRestfulInteraction.Create });
            configured.Rest.Single().Resource.Single().Profile = null;

            var result = supported.Intersect(configured, strictConfig: true);

            Assert.Equal(referenceUrl, result.Rest.Single().Resource.Single().Profile.Url.ToString());
        }

        [Fact]
        public void GivenACapabilityStatement_WhenIntersectingResourceProfiles_ThenConfiguredOverridesDefault()
        {
            var referenceUrl = "https://bing.com/";

            var supported = GetMockedListedCapabilityStatement();
            supported.TryAddRestInteraction(ResourceType.Observation, TypeRestfulInteraction.Create);

            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(configured, ResourceType.Observation, new[] { TypeRestfulInteraction.Create });
            configured.Rest.Single().Resource.Single().Profile = new ResourceReference("https://bing.com/");

            var result = supported.Intersect(configured, strictConfig: true);

            Assert.Equal(referenceUrl, result.Rest.Single().Resource.Single().Profile.Url.ToString());
        }

        [Fact]
        public void GivenACapabilityStatement_WhenIntersectingResourceProfiles_ThenConfiguredInteractionDocoOverridesDefault()
        {
            var doco = "Make stuff";

            var supported = GetMockedListedCapabilityStatement();
            supported.TryAddRestInteraction(ResourceType.Observation, TypeRestfulInteraction.Create);

            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(configured, ResourceType.Observation, new[] { TypeRestfulInteraction.Create });
            configured.Rest.Single().Resource.Single().Interaction.Single().Documentation = "Make stuff";

            var result = supported.Intersect(configured, strictConfig: true);

            Assert.Equal(doco, result.Rest.Single().Resource.Single().Interaction.Single().Documentation);
        }

        [Fact]
        public void GivenACapabilityStatement_WhenSupportingXmlJsonAndConfiguringJson_ThenResultIsCorrectlyIntersected()
        {
            var supported = GetMockedListedCapabilityStatement();
            supported.Format = new[] { "json", "xml" }.ToList();

            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            configured.Format = new[] { "json" }.ToList();

            var result = supported.Intersect(configured, strictConfig: true);

            Assert.Equal("json", result.Format.Single());
        }

        [Fact]
        public void GivenACapabilityStatement_WhenSupportingAcceptUnknown_ThenThrowsNotSupportedException()
        {
            var supported = GetMockedListedCapabilityStatement();
            supported.AcceptUnknown = new[] { UnknownContentCode.Extensions };

            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            configured.AcceptUnknown = UnknownContentCode.Both;

            Assert.Throws<UnsupportedConfigurationException>(() => supported.Intersect(configured, strictConfig: true));
        }

        [Fact]
        public void GivenACapabilityStatement_WhenSupportingAcceptUnknownExtensionsButNotBothAndNotStrict_ThenReturnsExtensionsAsDefault()
        {
            var supported = GetMockedListedCapabilityStatement();
            supported.AcceptUnknown = new[] { UnknownContentCode.Extensions };

            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            configured.AcceptUnknown = UnknownContentCode.Both;

            var result = supported.Intersect(configured, strictConfig: false);
            Assert.NotNull(result);
            Assert.Equal(UnknownContentCode.Extensions, result.AcceptUnknown);
        }

        [Fact]
        public void GivenACapabilityStatement_WhenSupportingAcceptUnknownExtensionsAndBothButNotBothStrict_ThenReturnsExtensions()
        {
            var supported = GetMockedListedCapabilityStatement();
            supported.AcceptUnknown = new[] { UnknownContentCode.Extensions, UnknownContentCode.Both };

            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            configured.AcceptUnknown = UnknownContentCode.Extensions;

            var result = supported.Intersect(configured, strictConfig: true);
            Assert.NotNull(result);
            Assert.Equal(UnknownContentCode.Extensions, result.AcceptUnknown);
        }

        [Fact]
        public void GivenACapabilityStatement_WhenDatesArePresent_ThenLatestDateIsSelected()
        {
            var supported = GetMockedListedCapabilityStatement();

            var date = DateTimeOffset.Parse("2000-01-01").ToString("o", CultureInfo.InvariantCulture);
            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            configured.Date = date;

            var result = supported.Intersect(configured, strictConfig: true);

            Assert.NotNull(result.Date);
            Assert.Equal(date, result.Date);
        }

        [Fact]
        public void GivenACapabilityStatement_WhenSupportingNoSearchParamConfiguringSearchParam_ThenNotSupportedExceptionIsThrown()
        {
            var supported = GetMockedListedCapabilityStatement();
            supported.TryAddSearchParams(ResourceType.Account, new List<SearchParamComponent>());

            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(configured, ResourceType.Account, null, GetSearchParamCollection());

            Assert.Throws<UnsupportedConfigurationException>(() => supported.Intersect(configured, strictConfig: true));
        }

        [Fact]
        public void GivenACapabilityStatement_WhenSupportingSearchParamConfiguringWrongSearchParam_ThenNotSupportedExceptionIsThrown()
        {
            var supported = GetMockedListedCapabilityStatement();
            supported.TryAddSearchParams(ResourceType.Account, GetSearchParamCollection());

            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(configured, ResourceType.Account, null, new List<SearchParamComponent> { new SearchParamComponent { Type = SearchParamType.Date, Name = "name" } });

            Assert.Throws<UnsupportedConfigurationException>(() => supported.Intersect(configured, strictConfig: true));
        }

        [Fact]
        public void GivenACapabilityStatement_WhenSupportingSearchParamConfiguringDuplicateSearchParams_ThenNotSupportedExceptionIsThrown()
        {
            var supported = GetMockedListedCapabilityStatement();
            supported.TryAddSearchParams(ResourceType.Account, GetSearchParamCollection());

            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(configured, ResourceType.Account, null, new List<SearchParamComponent>
            {
                new SearchParamComponent { Type = SearchParamType.String, Name = "name" },
                new SearchParamComponent { Type = SearchParamType.String, Name = "name" },
            });

            Assert.Throws<UnsupportedConfigurationException>(() => supported.Intersect(configured, strictConfig: true));
        }

        [Fact]
        public void GivenACapabilityStatement_WhenSupportingSearchParamConfiguringSearchParam_ThenResultIsCorrectlyIntersected()
        {
            var supported = GetMockedListedCapabilityStatement();
            supported.TryAddSearchParams(ResourceType.Account, GetSearchParamCollection());

            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(configured, ResourceType.Account, null, GetSearchParamCollection());

            var result = supported.Intersect(configured, strictConfig: true);

            Assert.NotEmpty(result.Rest.Single().Resource.First().SearchParam);
            Assert.Equal(SearchParamType.String, result.Rest.Single().Resource.First().SearchParam.First().Type);
            Assert.Equal("name", result.Rest.Single().Resource.First().SearchParam.First().Name);
        }

        [Fact]
        public void GivenACapabilityStatement_WhenSupportingSearchParamConfiguringNone_ThenResultIsCorrectlyIntersected()
        {
            var supported = GetMockedListedCapabilityStatement();
            supported.TryAddSearchParams(ResourceType.Account, GetSearchParamCollection());

            var configured = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(configured, ResourceType.Account, null);

            var result = supported.Intersect(configured, strictConfig: true);

            Assert.Empty(result.Rest.Single().Resource.First().SearchParam);
        }

        private ListedCapabilityStatement GetMockedListedCapabilityStatement()
        {
            return new ListedCapabilityStatement
            {
                Rest = new List<ListedRestComponent>
                {
                    new ListedRestComponent
                    {
                        Resource = new List<ListedResourceComponent>(),
                    },
                },
            };
        }

        private static List<SearchParamComponent> GetSearchParamCollection()
        {
            return new List<SearchParamComponent>
            {
                new SearchParamComponent { Type = SearchParamType.String, Name = "name" },
            };
        }
    }
}
