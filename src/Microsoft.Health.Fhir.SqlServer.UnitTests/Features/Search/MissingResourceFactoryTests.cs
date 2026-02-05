// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    /// <summary>
    /// Unit tests for MissingResourceFactory.
    /// Tests the JSON generation for OperationOutcome when a resource is not available.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class MissingResourceFactoryTests
    {
        [Fact]
        public void GivenResourceIdAndType_WhenCreateJson_ThenReturnsValidOperationOutcomeJson()
        {
            // Arrange
            const string resourceId = "patient-123";
            const string resourceType = "Patient";
            const string severity = "error";
            const string code = "not-found";

            // Act
            var json = MissingResourceFactory.CreateJson(resourceId, resourceType, severity, code);

            // Assert - verify it's valid JSON by parsing
            var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.Equal("OperationOutcome", root.GetProperty("resourceType").GetString());
            Assert.Equal(resourceId, root.GetProperty("id").GetString());

            var issue = root.GetProperty("issue")[0];
            Assert.Equal(severity, issue.GetProperty("severity").GetString());
            Assert.Equal(code, issue.GetProperty("code").GetString());
            Assert.Contains(resourceType, issue.GetProperty("diagnostics").GetString());
            Assert.Contains(resourceId, issue.GetProperty("diagnostics").GetString());
            Assert.Equal(resourceType, issue.GetProperty("expression")[0].GetString());
        }

        [Theory]
        [InlineData("error", "not-found")]
        [InlineData("warning", "informational")]
        [InlineData("information", "suppressed")]
        public void GivenDifferentSeverityAndCode_WhenCreateJson_ThenOutputReflectsValues(string severity, string code)
        {
            // Arrange
            const string resourceId = "obs-456";
            const string resourceType = "Observation";

            // Act
            var json = MissingResourceFactory.CreateJson(resourceId, resourceType, severity, code);

            // Assert
            var document = JsonDocument.Parse(json);
            var issue = document.RootElement.GetProperty("issue")[0];
            Assert.Equal(severity, issue.GetProperty("severity").GetString());
            Assert.Equal(code, issue.GetProperty("code").GetString());
        }

        [Fact]
        public void GivenSpecialCharactersInResourceId_WhenCreateJson_ThenProducesValidJson()
        {
            // Arrange - resource IDs can have hyphens and numbers
            const string resourceId = "example-resource-12345-abc";
            const string resourceType = "Encounter";
            const string severity = "error";
            const string code = "not-found";

            // Act
            var json = MissingResourceFactory.CreateJson(resourceId, resourceType, severity, code);

            // Assert - should still parse as valid JSON
            var document = JsonDocument.Parse(json);
            Assert.Equal(resourceId, document.RootElement.GetProperty("id").GetString());
        }

        [Theory]
        [InlineData("Patient")]
        [InlineData("Observation")]
        [InlineData("Encounter")]
        [InlineData("DiagnosticReport")]
        public void GivenDifferentResourceTypes_WhenCreateJson_ThenExpressionContainsResourceType(string resourceType)
        {
            // Arrange
            const string resourceId = "test-123";
            const string severity = "error";
            const string code = "not-found";

            // Act
            var json = MissingResourceFactory.CreateJson(resourceId, resourceType, severity, code);

            // Assert
            var document = JsonDocument.Parse(json);
            var expression = document.RootElement.GetProperty("issue")[0].GetProperty("expression")[0].GetString();
            Assert.Equal(resourceType, expression);
        }

        [Fact]
        public void GivenResourceDetails_WhenCreateJson_ThenDiagnosticsContainsResourceNotAvailableMessage()
        {
            // Arrange
            const string resourceId = "missing-resource-1";
            const string resourceType = "MedicationRequest";
            const string severity = "error";
            const string code = "not-found";

            // Act
            var json = MissingResourceFactory.CreateJson(resourceId, resourceType, severity, code);

            // Assert
            var document = JsonDocument.Parse(json);
            var diagnostics = document.RootElement.GetProperty("issue")[0].GetProperty("diagnostics").GetString();

            // Diagnostics should indicate the resource is not available
            Assert.Contains(resourceType, diagnostics);
            Assert.Contains(resourceId, diagnostics);
        }
    }
}
