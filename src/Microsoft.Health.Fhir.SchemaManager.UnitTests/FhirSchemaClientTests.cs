// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Manager;
using Microsoft.Health.SqlServer.Features.Schema.Manager.Model;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SchemaManager.UnitTests;

public class FhirSchemaClientTests
{
    private readonly IScriptProvider _scriptProvider = Substitute.For<IScriptProvider>();
    private readonly ISchemaDataStore _schemaDataStore = Substitute.For<ISchemaDataStore>();
    private readonly ISchemaManagerDataStore _schemaManagerDataStore = Substitute.For<ISchemaManagerDataStore>();

    [Fact]
    public async void GivenCurrentVersionAboveOne_GetAvailableVersions_ShouldReturnCorrectVersionsAsync()
    {
        // Arrange
        int currentVersion = 5;
        _schemaManagerDataStore.GetCurrentSchemaVersionAsync(default).Returns(currentVersion);

        var fhirSchemaClient = new FhirSchemaClient(_scriptProvider, _schemaDataStore, _schemaManagerDataStore);

        // Act
        List<AvailableVersion>? actualVersions = await fhirSchemaClient.GetAvailabilityAsync();

        int numberOfAvailableVersions = SchemaVersionConstants.Max - currentVersion + 1;
        var expectedVersions = Enumerable
            .Range(currentVersion, numberOfAvailableVersions)
            .Select(version => new AvailableVersion(version, $"{version}.sql", $"{version}.diff.sql"))
            .ToList();

        // Assert
        Assert.Equal(expectedVersions, actualVersions, new AvailableVersionEqualityCompare());
    }

    [Fact]
    public async void GivenCurrentVersionOfMax_GetAvailableVersionsShouldReturnOneVersion()
    {
        // Arrange
        _schemaManagerDataStore.GetCurrentSchemaVersionAsync(default).Returns(SchemaVersionConstants.Max);
        var fhirSchemaClient = new FhirSchemaClient(_scriptProvider, _schemaDataStore, _schemaManagerDataStore);

        // Act
        List<AvailableVersion>? actualVersions = await fhirSchemaClient.GetAvailabilityAsync();

        var expectedVersions = new List<AvailableVersion>()
        {
            new AvailableVersion(SchemaVersionConstants.Max, $"{SchemaVersionConstants.Max}.sql", $"{SchemaVersionConstants.Max}.diff.sql"),
        };

        // Assert
        Assert.Equal(expectedVersions, actualVersions, new AvailableVersionEqualityCompare());
    }

    private class AvailableVersionEqualityCompare : IEqualityComparer<AvailableVersion>
    {
        public bool Equals(AvailableVersion? x, AvailableVersion? y)
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
