// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.FhirPackages;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa.Features.FhirPackages;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.FhirPackages
{
    /// <summary>
    /// Runs a real package tarball through the reader, installer and SearchParameter handler, with only the
    /// SearchParameter infrastructure and the writer substituted.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.CustomSearch)]
    public class FhirPackageInstallEndToEndTests
    {
        private const string PackageId = "example.fhir.searchparameters";
        private const string PackageVersion = "1.0.0";

        private readonly ISearchParameterSupportResolver _supportResolver = Substitute.For<ISearchParameterSupportResolver>();
        private readonly IDataStoreSearchParameterValidator _dataStoreValidator = Substitute.For<IDataStoreSearchParameterValidator>();
        private readonly ISearchParameterPackageWriter _writer = Substitute.For<ISearchParameterPackageWriter>();

        public FhirPackageInstallEndToEndTests()
        {
            _supportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>()).Returns((true, false));
            _dataStoreValidator.ValidateSearchParameter(Arg.Any<SearchParameterInfo>(), out Arg.Any<string>()).Returns(true);
        }

        [Fact]
        public async Task GivenAnNpmPackedSearchParameterPackage_WhenReadAndInstalled_ThenOnlyTheSearchParameterIsWrittenAndTheRestIsReported()
        {
            // Arrange
            byte[] tarball = EmbeddedResourceManager.GetBytesContent("TestFiles", "FhirPackage-SearchParameters", "tgz");
            var descriptor = new FhirPackageDescriptor(PackageId, PackageVersion, Integrity(tarball));

            // Act
            FhirPackage package = await ReadAsync(tarball, descriptor);
            FhirPackageInstallResult result = await CreateInstaller().InstallAsync(package, CancellationToken.None);

            // Assert
            Assert.Equal(new[] { "http://example.org/fhir/SearchParameter/patient-nickname" }, result.InstalledResources[KnownResourceTypes.SearchParameter]);
            Assert.Equal(1, result.IgnoredResourceTypes["StructureDefinition"]);
            Assert.Equal(1, result.IgnoredResourceTypes["Patient"]);
            Assert.Equal(2, result.IgnoredResourceTypes.Count);
            Assert.Same(descriptor, result.Descriptor);
            await _writer.Received(1).WriteAsync(
                package,
                Arg.Is<IReadOnlyList<ResourceElement>>(x => x.Count == 1 && x[0].InstanceType == KnownResourceTypes.SearchParameter),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenATamperedTarball_WhenRead_ThenNothingReachesTheInstallerOrTheWriter()
        {
            // Arrange
            byte[] tarball = EmbeddedResourceManager.GetBytesContent("TestFiles", "FhirPackage-SearchParameters", "tgz");
            var descriptor = new FhirPackageDescriptor(PackageId, PackageVersion, Integrity(tarball));
            byte[] tampered = tarball.ToArray();
            tampered[tampered.Length / 2] ^= 0xFF;

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => ReadAsync(tampered, descriptor));

            // Assert
            Assert.Equal("Security", Assert.Single(exception.Issues).Code);
            await _writer.DidNotReceiveWithAnyArgs().WriteAsync(default, default, default, default);
        }

        private static IgnixaFhirPackageReader CreateReader() =>
            new IgnixaFhirPackageReader(new VersionSpecificModelInfoProvider(), Options.Create(new FhirPackageReaderOptions()), NullLoggerFactory.Instance);

        private static async Task<FhirPackage> ReadAsync(byte[] tarball, FhirPackageDescriptor descriptor)
        {
            using var stream = new MemoryStream(tarball);
            return await CreateReader().ReadAsync(stream, descriptor, CancellationToken.None);
        }

        private FhirPackageInstaller CreateInstaller()
        {
            var handler = new SearchParameterPackageHandler(
                Substitute.For<ISearchParameterDefinitionManager>(),
                Substitute.For<ISearchParameterOperations>(),
                Substitute.For<IModelInfoProvider>(),
                Substitute.For<ISearchParameterComparer<SearchParameterInfo>>(),
                _supportResolver,
                _dataStoreValidator,
                new ModelAttributeValidator(),
                _writer,
                NullLogger<SearchParameterPackageHandler>.Instance);

            return new FhirPackageInstaller(new[] { handler }, NullLogger<FhirPackageInstaller>.Instance);
        }

        private static string Integrity(byte[] tarball) => "sha512-" + Convert.ToBase64String(SHA512.HashData(tarball));
    }
}
