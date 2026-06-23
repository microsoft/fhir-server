// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance.Models
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class ListedCapabilityStatementTests
    {
        [Fact]
        public void GivenANewInstance_WhenCreated_ThenAllPropertiesShouldBeInitializedCorrectly()
        {
            // Arrange & Act
            var statement = new ListedCapabilityStatement();

            // Assert
            Assert.NotNull(statement.Id);
            Assert.NotEmpty(statement.Id);
            Assert.Equal("CapabilityStatement", statement.ResourceType);
            Assert.NotNull(statement.Status);
            Assert.Empty(statement.Status);
            Assert.NotNull(statement.Kind);
            Assert.Empty(statement.Kind);
            Assert.NotNull(statement.Rest);
            Assert.Empty(statement.Rest);
            Assert.NotNull(statement.Format);
            Assert.Empty(statement.Format);
            Assert.NotNull(statement.PatchFormat);
            Assert.Empty(statement.PatchFormat);
            Assert.NotNull(statement.AdditionalData);
            Assert.Empty(statement.AdditionalData);
            Assert.NotNull(statement.Profile);
            Assert.Empty(statement.Profile);
        }

        [Fact]
        public void GivenAStatement_WhenResourceTypeIsAccessed_ThenItShouldReturnCapabilityStatement()
        {
            // Arrange
            var statement = new ListedCapabilityStatement();

            // Act
            string resourceType = statement.ResourceType;

            // Assert
            Assert.Equal("CapabilityStatement", resourceType);
        }

        [Fact]
        public void GivenAStatement_WhenPropertiesAreSet_ThenTheyShouldRetainValues()
        {
            // Arrange
            var statement = new ListedCapabilityStatement();
            var testUrl = new Uri("https://example.com/fhir");
            var testVersion = "1.0.0";
            var testName = "TestCapabilityStatement";
            var testPublisher = "Microsoft";
            var testDate = "2024-01-01";
            var testFhirVersion = "4.0.1";

            // Act
            statement.Url = testUrl;
            statement.Version = testVersion;
            statement.Name = testName;
            statement.Publisher = testPublisher;
            statement.Experimental = true;
            statement.Date = testDate;
            statement.FhirVersion = testFhirVersion;

            // Assert
            Assert.Equal(testUrl, statement.Url);
            Assert.Equal(testVersion, statement.Version);
            Assert.Equal(testName, statement.Name);
            Assert.Equal(testPublisher, statement.Publisher);
            Assert.True(statement.Experimental);
            Assert.Equal(testDate, statement.Date);
            Assert.Equal(testFhirVersion, statement.FhirVersion);
        }

        [Fact]
        public void GivenAStatement_WhenSoftwareIsSet_ThenItShouldRetainValues()
        {
            // Arrange
            var statement = new ListedCapabilityStatement();
            var software = new SoftwareComponent
            {
                Name = "Microsoft FHIR Server",
                Version = "1.0.0",
            };

            // Act
            statement.Software = software;

            // Assert
            Assert.NotNull(statement.Software);
            Assert.Equal("Microsoft FHIR Server", statement.Software.Name);
            Assert.Equal("1.0.0", statement.Software.Version);
        }

        [Fact]
        public void GivenAStatement_WhenCollectionsAreModified_ThenChangesShouldPersist()
        {
            // Arrange
            var statement = new ListedCapabilityStatement();

            // Act
            statement.Status.Add("active");
            statement.Kind.Add("instance");
            statement.Format.Add("application/fhir+json");
            statement.Format.Add("application/fhir+xml");
            statement.PatchFormat.Add("application/json-patch+json");

            var restComponent = new ListedRestComponent { Mode = "server" };
            statement.Rest.Add(restComponent);

            var profile = new ReferenceComponent { Reference = "http://example.com/profile" };
            statement.Profile.Add(profile);

            // Assert
            Assert.Contains("active", statement.Status);
            Assert.Contains("instance", statement.Kind);
            Assert.Contains("application/fhir+json", statement.Format);
            Assert.Contains("application/fhir+xml", statement.Format);
            Assert.Contains("application/json-patch+json", statement.PatchFormat);
            Assert.Single(statement.Rest);
            Assert.Equal("server", statement.Rest.First().Mode);
            Assert.Single(statement.Profile);
            Assert.Equal("http://example.com/profile", statement.Profile.First().Reference);
        }

        [Fact]
        public void GivenAStatement_WhenAdditionalDataIsSet_ThenItShouldRetainValues()
        {
            // Arrange
            var statement = new ListedCapabilityStatement();
            var customKey = "custom-extension";
            var customValue = JToken.FromObject("custom-value");

            // Act
            statement.AdditionalData[customKey] = customValue;

            // Assert
            Assert.Single(statement.AdditionalData);
            Assert.True(statement.AdditionalData.ContainsKey(customKey));
            Assert.Equal(customValue, statement.AdditionalData[customKey]);
        }
    }
}
