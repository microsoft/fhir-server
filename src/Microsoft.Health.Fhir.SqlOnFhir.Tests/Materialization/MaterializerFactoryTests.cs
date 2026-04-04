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
    public void GivenFabricTarget_WhenDeltaLakeNotConfigured_ThenFallsBackToParquet()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, _parquetMaterializer, deltaLakeMaterializer: null);

        var result = factory.GetMaterializers(MaterializationTarget.Fabric);

        Assert.Single(result);
        Assert.Same(_parquetMaterializer, result[0]);
    }

    [Fact]
    public void GivenFabricTarget_WhenNeitherConfigured_ThenFallsBackToSql()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, parquetMaterializer: null, deltaLakeMaterializer: null);

        var result = factory.GetMaterializers(MaterializationTarget.Fabric);

        Assert.Single(result);
        Assert.Same(_sqlMaterializer, result[0]);
    }

    [Fact]
    public void GivenParquetTargetWithoutParquetMaterializer_WhenGetMaterializers_ThenFallsBackToSql()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, parquetMaterializer: null);

        var result = factory.GetMaterializers(MaterializationTarget.Parquet);

        Assert.Single(result);
        Assert.Same(_sqlMaterializer, result[0]);
    }

    [Fact]
    public void GivenNoneTarget_WhenGetMaterializers_ThenFallsBackToSql()
    {
        var factory = new MaterializerFactory(_sqlMaterializer, _config, NullLogger<MaterializerFactory>.Instance, _parquetMaterializer, _deltaLakeMaterializer);

        var result = factory.GetMaterializers(MaterializationTarget.None);

        Assert.Single(result);
        Assert.Same(_sqlMaterializer, result[0]);
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
}
