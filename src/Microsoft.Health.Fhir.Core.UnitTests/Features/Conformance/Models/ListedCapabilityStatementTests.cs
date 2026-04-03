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

        [Fact]
        public void GivenAStatement_WhenCloned_ThenAllPropertiesShouldBeCopied()
        {
            // Arrange
            var original = new ListedCapabilityStatement
            {
                Url = new Uri("https://example.com/fhir"),
                Version = "1.0.0",
                Name = "TestStatement",
                Publisher = "Microsoft",
                Experimental = true,
                Date = "2024-01-01",
                FhirVersion = "4.0.1",
                Software = new SoftwareComponent
                {
                    Name = "Microsoft FHIR Server",
                    Version = "1.0.0",
                },
            };

            original.Status.Add("active");
            original.Kind.Add("instance");
            original.Format.Add("application/fhir+json");
            original.PatchFormat.Add("application/json-patch+json");
            original.Rest.Add(new ListedRestComponent { Mode = "server" });
            original.Profile.Add(new ReferenceComponent { Reference = "http://example.com/profile" });
            original.AdditionalData["custom"] = JToken.FromObject("value");

            // Act
            var clone = original.Clone();

            // Assert
            Assert.NotSame(original, clone);
            Assert.Equal(original.Url, clone.Url);
            Assert.Equal(original.Version, clone.Version);
            Assert.Equal(original.Name, clone.Name);
            Assert.Equal(original.Publisher, clone.Publisher);
            Assert.Equal(original.Experimental, clone.Experimental);
            Assert.Equal(original.Date, clone.Date);
            Assert.Equal(original.FhirVersion, clone.FhirVersion);
            Assert.NotSame(original.Software, clone.Software);
            Assert.Equal(original.Software.Name, clone.Software.Name);
            Assert.Equal(original.Software.Version, clone.Software.Version);
        }

        [Fact]
        public void GivenAStatement_WhenCloned_ThenCollectionsShouldBeCopied()
        {
            // Arrange
            var original = new ListedCapabilityStatement();
            original.Status.Add("active");
            original.Kind.Add("instance");
            original.Format.Add("application/fhir+json");
            original.PatchFormat.Add("application/json-patch+json");
            original.Rest.Add(new ListedRestComponent { Mode = "server" });
            original.Profile.Add(new ReferenceComponent { Reference = "http://example.com/profile" });
            original.AdditionalData["custom"] = JToken.FromObject("value");

            // Act
            var clone = original.Clone();

            // Assert
            Assert.NotSame(original.Status, clone.Status);
            Assert.Equal(original.Status.Count, clone.Status.Count);
            Assert.Contains("active", clone.Status);

            Assert.NotSame(original.Kind, clone.Kind);
            Assert.Equal(original.Kind.Count, clone.Kind.Count);
            Assert.Contains("instance", clone.Kind);

            Assert.NotSame(original.Format, clone.Format);
            Assert.Equal(original.Format.Count, clone.Format.Count);
            Assert.Contains("application/fhir+json", clone.Format);

            Assert.NotSame(original.PatchFormat, clone.PatchFormat);
            Assert.Equal(original.PatchFormat.Count, clone.PatchFormat.Count);
            Assert.Contains("application/json-patch+json", clone.PatchFormat);

            Assert.NotSame(original.Rest, clone.Rest);
            Assert.Equal(original.Rest.Count, clone.Rest.Count);
            Assert.Equal("server", clone.Rest.First().Mode);

            Assert.NotSame(original.Profile, clone.Profile);
            Assert.Equal(original.Profile.Count, clone.Profile.Count);
            Assert.Equal("http://example.com/profile", clone.Profile.First().Reference);

            Assert.NotSame(original.AdditionalData, clone.AdditionalData);
            Assert.Equal(original.AdditionalData.Count, clone.AdditionalData.Count);
            Assert.True(clone.AdditionalData.ContainsKey("custom"));
        }

        [Fact]
        public void GivenAStatement_WhenCloned_ThenModifyingCloneShouldNotAffectOriginal()
        {
            // Arrange
            var original = new ListedCapabilityStatement
            {
                Name = "Original",
                Experimental = false,
            };
            original.Status.Add("active");

            // Act
            var clone = original.Clone();
            clone.Name = "Modified";
            clone.Experimental = true;
            clone.Status.Add("modified");

            // Assert
            Assert.Equal("Original", original.Name);
            Assert.False(original.Experimental);
            Assert.DoesNotContain("modified", original.Status);
            Assert.Equal("Modified", clone.Name);
            Assert.True(clone.Experimental);
            Assert.Contains("modified", clone.Status);
        }

        [Fact]
        public void GivenAStatementWithNullSoftware_WhenCloned_ThenCloneShouldHaveNullSoftwareProperties()
        {
            // Arrange
            var original = new ListedCapabilityStatement
            {
                Software = null,
            };

            // Act
            var clone = original.Clone();

            // Assert
            Assert.NotNull(clone.Software);
            Assert.Null(clone.Software.Name);
            Assert.Null(clone.Software.Version);
        }

        [Fact]
        public void GivenAStatement_WhenIdIsSet_ThenItShouldRetainValue()
        {
            // Arrange
            var statement = new ListedCapabilityStatement();
            var customId = "custom-id-12345";

            // Act
            statement.Id = customId;

            // Assert
            Assert.Equal(customId, statement.Id);
        }

        [Fact]
        public async Task GivenAStatement_WhenClonedWhileCollectionsAreModified_ThenNoExceptionShouldBeThrown()
        {
            // This test simulates concurrent cloning and modification of the ListedCapabilityStatement to ensure thread safety of the clone operation.

            // Arrange
            var statement = new ListedCapabilityStatement();

            // Pre-populate collections
            for (int i = 0; i < 10; i++)
            {
                statement.Status.Add($"status-{i}");
                statement.Kind.Add($"kind-{i}");
                statement.Format.Add($"format-{i}");
                statement.PatchFormat.Add($"patch-{i}");
                statement.Rest.Add(new ListedRestComponent { Mode = $"mode-{i}" });
                statement.Profile.Add(new ReferenceComponent { Reference = $"http://example.com/profile-{i}" });
                statement.AdditionalData[$"key-{i}"] = JToken.FromObject($"value-{i}");
            }

            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
            var cloneTask = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // Perform multiple clone operations to increase chance of concurrent access
                    for (int i = 0; i < 50; i++)
                    {
                        var clone = statement.Clone();
                        Assert.NotNull(clone);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            var modifyTask = Task.Run(() =>
            {
                try
                {
                    // Modify collections while cloning is happening
                    for (int i = 10; i < 60; i++)
                    {
                        statement.Status.Add($"status-{i}");
                        statement.Kind.Add($"kind-{i}");
                        statement.Format.Add($"format-{i}");
                        statement.PatchFormat.Add($"patch-{i}");
                        statement.Rest.Add(new ListedRestComponent { Mode = $"mode-{i}" });
                        statement.Profile.Add(new ReferenceComponent { Reference = $"http://example.com/profile-{i}" });
                        statement.AdditionalData[$"key-{i}"] = JToken.FromObject($"value-{i}");

                        // Small delay to allow clone operations to interleave
                        System.Threading.Thread.Sleep(1);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            // Act
            await Task.WhenAll(cloneTask, modifyTask);

            // Assert
            Assert.Empty(exceptions);
        }
    }
}
