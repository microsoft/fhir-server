// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Exceptions;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Manager;
using Microsoft.Health.SqlServer.Features.Schema.Manager.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Health.Fhir.SchemaManager.UnitTests;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Operations)]
public class FhirSchemaClientTests
{
    private readonly IScriptProvider _scriptProvider = Substitute.For<IScriptProvider>();
    private readonly ISchemaDataStore _schemaDataStore = Substitute.For<ISchemaDataStore>();
    private readonly ISchemaManagerDataStore _schemaManagerDataStore = Substitute.For<ISchemaManagerDataStore>();

    [Fact]
    public async Task GivenCurrentVersionAboveOne_GetAvailableVersions_ShouldReturnCorrectVersionsAsync()
    {
        // Arrange
        int currentVersion = 5;
        _schemaManagerDataStore.GetCurrentSchemaVersionAsync(TestContext.Current.CancellationToken).Returns(currentVersion);

        var fhirSchemaClient = new FhirSchemaClient(_scriptProvider, _schemaDataStore, _schemaManagerDataStore);

        // Act
        List<AvailableVersion> actualVersions = await fhirSchemaClient.GetAvailabilityAsync(TestContext.Current.CancellationToken);

        int numberOfAvailableVersions = SchemaVersionConstants.Max - currentVersion + 1;
        var expectedVersions = Enumerable
            .Range(currentVersion, numberOfAvailableVersions)
            .Select(version => new AvailableVersion(version, $"{version}.sql", $"{version}.diff.sql"))
            .ToList();

        // Assert
        Assert.Equal(expectedVersions, actualVersions, new AvailableVersionEqualityCompare());
    }

    [Fact]
    public async Task GivenCurrentVersionOfMax_GetAvailableVersionsShouldReturnOneVersion()
    {
        // Arrange
        _schemaManagerDataStore.GetCurrentSchemaVersionAsync(TestContext.Current.CancellationToken).Returns(SchemaVersionConstants.Max);
        var fhirSchemaClient = new FhirSchemaClient(_scriptProvider, _schemaDataStore, _schemaManagerDataStore);

        // Act
        List<AvailableVersion> actualVersions = await fhirSchemaClient.GetAvailabilityAsync(TestContext.Current.CancellationToken);

        var expectedVersions = new List<AvailableVersion>()
        {
            new AvailableVersion(SchemaVersionConstants.Max, $"{SchemaVersionConstants.Max}.sql", $"{SchemaVersionConstants.Max}.diff.sql"),
        };

        // Assert
        Assert.Equal(expectedVersions, actualVersions, new AvailableVersionEqualityCompare());
    }

    [Fact]
    public async Task GivenVersion1_GetAvailableVersionsShouldReturnEmptyDiffUriForVersion1()
    {
        // Arrange
        _schemaManagerDataStore.GetCurrentSchemaVersionAsync(TestContext.Current.CancellationToken).Returns(1);
        var fhirSchemaClient = new FhirSchemaClient(_scriptProvider, _schemaDataStore, _schemaManagerDataStore);

        // Act
        List<AvailableVersion> actualVersions = await fhirSchemaClient.GetAvailabilityAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(actualVersions, v => v.Id == 1 && string.IsNullOrEmpty(v.DiffUri));
    }

    [Fact]
    public async Task GivenCancellationRequested_WhenGetAvailability_ThenPropagatesCancellation()
    {
        // Arrange
        using (var cancellationTokenSource = new CancellationTokenSource())
        {
            cancellationTokenSource.Cancel();
            _schemaManagerDataStore.GetCurrentSchemaVersionAsync(cancellationTokenSource.Token)
                .ThrowsAsync(new TaskCanceledException());
            var fhirSchemaClient = new FhirSchemaClient(_scriptProvider, _schemaDataStore, _schemaManagerDataStore);

            // Act & Assert
            await Assert.ThrowsAsync<TaskCanceledException>(
                async () => await fhirSchemaClient.GetAvailabilityAsync(cancellationTokenSource.Token));
        }
    }

    [Fact]
    public async Task GivenCompatibleVersionsFound_WhenGetCompatibility_ThenReturnsCompatibleVersion()
    {
        // Arrange
        var compatibleVersions = new CompatibleVersions(1, 5);
        _schemaDataStore.GetLatestCompatibleVersionsAsync(TestContext.Current.CancellationToken).Returns(compatibleVersions);
        var fhirSchemaClient = new FhirSchemaClient(_scriptProvider, _schemaDataStore, _schemaManagerDataStore);

        // Act
        CompatibleVersion result = await fhirSchemaClient.GetCompatibilityAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, result.Min);
        Assert.Equal(5, result.Max);
    }

    [Fact]
    public async Task GivenCompatibleVersionsNotFound_WhenGetCompatibility_ThenReturnsDefaultVersion()
    {
        // Arrange
        _schemaDataStore.GetLatestCompatibleVersionsAsync(TestContext.Current.CancellationToken)
            .ThrowsAsync(new CompatibleVersionsNotFoundException("Compatible versions not found"));
        var fhirSchemaClient = new FhirSchemaClient(_scriptProvider, _schemaDataStore, _schemaManagerDataStore);

        // Act
        CompatibleVersion result = await fhirSchemaClient.GetCompatibilityAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, result.Min);
        Assert.Equal(SchemaVersionConstants.Max, result.Max);
    }

    [Fact]
    public async Task GivenCurrentVersionInfo_WhenGetCurrentVersionInformation_ThenReturnsCurrentVersionList()
    {
        // Arrange
        var currentVersionInfo = new List<CurrentVersionInformation>
        {
            new CurrentVersionInformation(1, (Microsoft.Health.SqlServer.Features.Schema.SchemaVersionStatus)2, new List<string> { "Server1", "Server2" }),
            new CurrentVersionInformation(2, (Microsoft.Health.SqlServer.Features.Schema.SchemaVersionStatus)2, new List<string> { "Server3" }),
        };
        _schemaDataStore.GetCurrentVersionAsync(TestContext.Current.CancellationToken).Returns(currentVersionInfo);
        var fhirSchemaClient = new FhirSchemaClient(_scriptProvider, _schemaDataStore, _schemaManagerDataStore);

        // Act
        List<CurrentVersion> result = await fhirSchemaClient.GetCurrentVersionInformationAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Id);
        Assert.NotNull(result[0].Status);
        Assert.Equal(2, result[0].Servers.Count);
        Assert.Equal("Server1", result[0].Servers[0]);
    }

    [Fact]
    public async Task GivenVersion_WhenGetDiffScript_ThenReturnsScriptFromProvider()
    {
        // Arrange
        const string expectedScript = "diff script content";
        _scriptProvider.GetMigrationScript(5, false).Returns(expectedScript);
        var fhirSchemaClient = new FhirSchemaClient(_scriptProvider, _schemaDataStore, _schemaManagerDataStore);

        // Act
        string result = await fhirSchemaClient.GetDiffScriptAsync(5, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedScript, result);
        _scriptProvider.Received(1).GetMigrationScript(5, false);
    }

    [Fact]
    public async Task GivenVersion_WhenGetScript_ThenReturnsScriptFromProvider()
    {
        // Arrange
        const string expectedScript = "full script content";
        _scriptProvider.GetMigrationScript(5, true).Returns(expectedScript);
        var fhirSchemaClient = new FhirSchemaClient(_scriptProvider, _schemaDataStore, _schemaManagerDataStore);

        // Act
        string result = await fhirSchemaClient.GetScriptAsync(5, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedScript, result);
        _scriptProvider.Received(1).GetMigrationScript(5, true);
    }

    [Fact]
    public void GivenNullScriptProvider_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new FhirSchemaClient(null, _schemaDataStore, _schemaManagerDataStore));
    }

    [Fact]
    public void GivenNullSchemaDataStore_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new FhirSchemaClient(_scriptProvider, null, _schemaManagerDataStore));
    }

    [Fact]
    public void GivenNullSchemaManagerDataStore_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new FhirSchemaClient(_scriptProvider, _schemaDataStore, null));
    }

    private class AvailableVersionEqualityCompare : IEqualityComparer<AvailableVersion>
    {
        public bool Equals(AvailableVersion x, AvailableVersion y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null)
            {
                return false;
            }

            if (y is null)
            {
                return false;
            }

            if (x.GetType() != y.GetType())
            {
                return false;
            }

            return x.Id == y.Id && x.ScriptUri == y.ScriptUri && x.DiffUri == y.DiffUri;
        }

        public int GetHashCode(AvailableVersion obj)
        {
            return HashCode.Combine(obj.Id, obj.ScriptUri, obj.DiffUri);
        }
    }
}
