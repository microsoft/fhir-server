// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.SqlOnFhir.Materialization;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlOnFhir.Tests.Materialization;

/// <summary>
/// Unit tests for <see cref="MaterializerFactory"/>.
/// </summary>
public class MaterializerFactoryTests
{
    private readonly IViewDefinitionMaterializer _sqlMaterializer;
    private readonly IViewDefinitionMaterializer _parquetMaterializer;
    private readonly IViewDefinitionMaterializer _deltaLakeMaterializer;
    private readonly IOptions<SqlOnFhirMaterializationConfiguration> _config;

    public MaterializerFactoryTests()
    {
        _sqlMaterializer = Substitute.For<IViewDefinitionMaterializer>();
        _parquetMaterializer = Substitute.For<IViewDefinitionMaterializer>();
        _deltaLakeMaterializer = Substitute.For<IViewDefinitionMaterializer>();

        _config = Options.Create(new SqlOnFhirMaterializationConfiguration
        {
            DefaultTarget = MaterializationTarget.SqlServer,
        });
    }

    [Fact]
    public void GivenSqlServerTarget_WhenGetMaterializers_ThenSqlMaterializerReturned()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, _parquetMaterializer, _deltaLakeMaterializer);

        var result = factory.GetMaterializers(MaterializationTarget.SqlServer);

        Assert.Single(result);
        Assert.Same(_sqlMaterializer, result[0]);
    }

    [Fact]
    public void GivenParquetTarget_WhenGetMaterializers_ThenParquetMaterializerReturned()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, _parquetMaterializer, _deltaLakeMaterializer);

        var result = factory.GetMaterializers(MaterializationTarget.Parquet);

        Assert.Single(result);
        Assert.Same(_parquetMaterializer, result[0]);
    }

    [Fact]
    public void GivenBothTargets_WhenGetMaterializers_ThenBothReturned()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, _parquetMaterializer, _deltaLakeMaterializer);

        var result = factory.GetMaterializers(MaterializationTarget.SqlServer | MaterializationTarget.Parquet);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GivenFabricTarget_WhenDeltaLakeConfigured_ThenDeltaLakeMaterializerUsed()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, _parquetMaterializer, _deltaLakeMaterializer);

        var result = factory.GetMaterializers(MaterializationTarget.Fabric);

        Assert.Single(result);
        Assert.Same(_deltaLakeMaterializer, result[0]);
    }

    [Fact]
    public void GivenFabricTarget_WhenDeltaLakeNotConfigured_ThenReturnsEmpty()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, _parquetMaterializer, deltaLakeMaterializer: null);

        var result = factory.GetMaterializers(MaterializationTarget.Fabric);

        Assert.Empty(result);
    }

    [Fact]
    public void GivenFabricTarget_WhenNeitherConfigured_ThenReturnsEmpty()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, parquetMaterializer: null, deltaLakeMaterializer: null);

        var result = factory.GetMaterializers(MaterializationTarget.Fabric);

        Assert.Empty(result);
    }

    [Fact]
    public void GivenParquetTargetWithoutParquetMaterializer_WhenGetMaterializers_ThenReturnsEmpty()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, parquetMaterializer: null);

        var result = factory.GetMaterializers(MaterializationTarget.Parquet);

        Assert.Empty(result);
    }

    [Fact]
    public void GivenNoneTarget_WhenGetMaterializers_ThenReturnsEmpty()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, _parquetMaterializer, _deltaLakeMaterializer);

        var result = factory.GetMaterializers(MaterializationTarget.None);

        Assert.Empty(result);
    }

    [Fact]
    public void GivenAllTargets_WhenGetMaterializers_ThenAllThreeReturned()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, _parquetMaterializer, _deltaLakeMaterializer);

        var result = factory.GetMaterializers(
            MaterializationTarget.SqlServer | MaterializationTarget.Parquet | MaterializationTarget.Fabric);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void DefaultTarget_ReturnsConfiguredValue()
    {
        var config = Options.Create(new SqlOnFhirMaterializationConfiguration
        {
            DefaultTarget = MaterializationTarget.Fabric,
        });

        var factory = new MaterializerFactory(_sqlMaterializer, config, NullLogger<MaterializerFactory>.Instance, _parquetMaterializer, _deltaLakeMaterializer);

        Assert.Equal(MaterializationTarget.Fabric, factory.DefaultTarget);
    }

    [Fact]
    public void GivenFabricTarget_WhenValidateTarget_AndDeltaLakeConfigured_ThenReturnsNull()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, _parquetMaterializer, _deltaLakeMaterializer);

        string? error = factory.ValidateTarget(MaterializationTarget.Fabric);

        Assert.Null(error);
    }

    [Fact]
    public void GivenFabricTarget_WhenValidateTarget_AndDeltaLakeNotConfigured_ThenReturnsError()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, _parquetMaterializer, deltaLakeMaterializer: null);

        string? error = factory.ValidateTarget(MaterializationTarget.Fabric);

        Assert.NotNull(error);
        Assert.Contains("Fabric", error);
        Assert.Contains("StorageAccountUri", error);
    }

    [Fact]
    public void GivenParquetTarget_WhenValidateTarget_AndParquetNotConfigured_ThenReturnsError()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, parquetMaterializer: null);

        string? error = factory.ValidateTarget(MaterializationTarget.Parquet);

        Assert.NotNull(error);
        Assert.Contains("Parquet", error);
    }

    [Fact]
    public void GivenSqlServerTarget_WhenValidateTarget_ThenReturnsNull()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance);

        string? error = factory.ValidateTarget(MaterializationTarget.SqlServer);

        Assert.Null(error);
    }

    [Fact]
    public void GivenNoneTarget_WhenValidateTarget_ThenReturnsError()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, _parquetMaterializer, _deltaLakeMaterializer);

        string? error = factory.ValidateTarget(MaterializationTarget.None);

        Assert.NotNull(error);
    }

    [Fact]
    public async Task GivenFabricTarget_WhenUpsertResourceAsync_AndNoMaterializers_ThenThrowsInvalidOperationException()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, parquetMaterializer: null, deltaLakeMaterializer: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.UpsertResourceAsync(
                MaterializationTarget.Fabric,
                "{}",
                "test_view",
                null!,
                "Patient/1",
                CancellationToken.None));
    }
}
