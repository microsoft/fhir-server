// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.FhirPackages;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa.Features.FhirPackages;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.FhirPackages
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.CustomSearch)]
    public class IgnixaFhirPackageReaderTests
    {
        private const string PackageId = "example.package";
        private const string PackageVersion = "1.0.0";

        private readonly IModelInfoProvider _modelInfoProvider = new VersionSpecificModelInfoProvider();

        private string ServerFhirVersion => _modelInfoProvider.SupportedVersion.VersionString;

        private string CorePackageId => _modelInfoProvider.Version switch
        {
            FhirSpecification.Stu3 => "hl7.fhir.r3.core",
            FhirSpecification.R4 => "hl7.fhir.r4.core",
            FhirSpecification.R4B => "hl7.fhir.r4b.core",
            _ => "hl7.fhir.r5.core",
        };

        [Fact]
        public async Task GivenAValidSearchParameterPackage_WhenRead_ThenItsIdentityAndResourcesAreReturned()
        {
            // Arrange
            byte[] tarball = NewPackage().File("package/SearchParameter-a.json", SearchParameterJson()).Build();

            // Act
            FhirPackage package = await CreateReader().ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None);

            // Assert
            Assert.Equal(PackageId, package.PackageId);
            Assert.Equal(PackageVersion, package.Version);
            Assert.Equal(Integrity(tarball), package.Integrity);
            FhirPackageEntry entry = Assert.Single(package.ResourceEntries);
            Assert.Equal("package/SearchParameter-a.json", entry.Path);
            Assert.Equal(KnownResourceTypes.SearchParameter, entry.ResourceType);
            Assert.Empty(package.IgnoredEntries);
            Assert.Empty(package.IgnoredDependencies);
        }

        [Fact]
        public async Task GivenAPackageWithOtherContent_WhenRead_ThenRootResourcesAreCandidatesAndEverythingElseIsClassified()
        {
            // Arrange
            byte[] tarball = NewPackage()
                .File("package/SearchParameter-a.json", SearchParameterJson())
                .File("package/StructureDefinition-x.json", "{\"resourceType\":\"StructureDefinition\"}")
                .File("package/ValueSet-x.json", "{\"resourceType\":\"ValueSet\"}")
                .File("package/example/Patient-1.json", "{\"resourceType\":\"Patient\"}")
                .File("package/other/SearchParameter-b.json", SearchParameterJson())
                .File("package/.index.json", "{\"index-version\":1,\"files\":[]}")
                .File("package/README.md", "not json")
                .Build();

            // Act
            FhirPackage package = await CreateReader().ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None);

            // Assert
            Assert.Equal(
                new[] { "SearchParameter", "StructureDefinition", "ValueSet" },
                package.ResourceEntries.Select(x => x.ResourceType).OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal(
                new[] { "package/example/Patient-1.json", "package/other/SearchParameter-b.json" },
                package.IgnoredEntries.Select(x => x.Path).OrderBy(x => x, StringComparer.Ordinal));
        }

        [Fact]
        public async Task GivenNonCoreDependencies_WhenRead_ThenTheyAreReportedAndNotResolved()
        {
            // Arrange
            byte[] tarball = NewPackage(manifest => manifest["dependencies"]["example.other"] = "2.1.0")
                .File("package/SearchParameter-a.json", SearchParameterJson())
                .Build();

            // Act
            FhirPackage package = await CreateReader().ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None);

            // Assert
            Assert.Equal(new[] { "example.other@2.1.0" }, package.IgnoredDependencies);
        }

        [Fact]
        public async Task GivenATarballThatDoesNotMatchItsIntegrity_WhenRead_ThenItIsRejectedAsASecurityIssue()
        {
            // Arrange
            byte[] tarball = NewPackage().File("package/SearchParameter-a.json", SearchParameterJson()).Build();
            byte[] other = NewPackage().File("package/SearchParameter-b.json", SearchParameterJson()).Build();

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader().ReadAsync(new MemoryStream(tarball), new FhirPackageDescriptor(PackageId, PackageVersion, Integrity(other)), CancellationToken.None));

            // Assert
            Assert.Equal("Security", Assert.Single(exception.Issues).Code);
        }

        [Theory]
        [InlineData("not-an-sri-value")]
        [InlineData("sha1-2jmj7l5rSw0yVb/vlWAYkK/YBwk=")]
        [InlineData("sha256-47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=")]
        [InlineData("sha512-tooShort")]
        public async Task GivenAnExpectedIntegrityThatIsNotASingleSha512Value_WhenRead_ThenItIsRejected(string integrity)
        {
            // Arrange
            byte[] tarball = NewPackage().File("package/SearchParameter-a.json", SearchParameterJson()).Build();

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader().ReadAsync(new MemoryStream(tarball), new FhirPackageDescriptor(PackageId, PackageVersion, integrity), CancellationToken.None));

            // Assert
            Assert.Equal("Security", Assert.Single(exception.Issues).Code);
        }

        [Fact]
        public async Task GivenATarballLargerThanTheCompressedLimit_WhenRead_ThenItIsRejectedBeforeExtraction()
        {
            // Arrange
            byte[] tarball = NewPackage().File("package/SearchParameter-a.json", SearchParameterJson()).Build();
            var options = new FhirPackageReaderOptions { MaxCompressedBytes = tarball.Length - 1 };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader(options).ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None));

            // Assert
            Assert.Equal("TooCostly", Assert.Single(exception.Issues).Code);
        }

        [Fact]
        public async Task GivenAnArchiveLargerThanTheExpandedLimit_WhenRead_ThenItIsRejected()
        {
            // Arrange
            byte[] tarball = NewPackage().File("package/SearchParameter-a.json", SearchParameterJson()).Build();
            var options = new FhirPackageReaderOptions { MaxExpandedBytes = 1024 };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader(options).ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("TooCostly", issue.Code);
            Assert.Contains("ExpandedSizeLimit", issue.Diagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenAnEntryLargerThanTheEntryLimit_WhenRead_ThenItIsRejected()
        {
            // Arrange
            byte[] tarball = NewPackage().File("package/SearchParameter-a.json", SearchParameterJson(description: new string('x', 2048))).Build();
            var options = new FhirPackageReaderOptions { MaxEntryBytes = 1024 };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader(options).ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("TooCostly", issue.Code);
            Assert.Contains("EntrySizeLimit", issue.Diagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenMoreEntriesThanTheEntryCountLimit_WhenRead_ThenItIsRejected()
        {
            // Arrange
            byte[] tarball = NewPackage()
                .File("package/SearchParameter-a.json", SearchParameterJson())
                .File("package/SearchParameter-b.json", SearchParameterJson())
                .Build();
            var options = new FhirPackageReaderOptions { MaxEntries = 2 };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader(options).ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("TooCostly", issue.Code);
            Assert.Contains("EntryCountLimit", issue.Diagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenJsonDeeperThanTheDepthLimit_WhenRead_ThenItIsRejected()
        {
            // Arrange
            string deep = "{\"resourceType\":\"SearchParameter\",\"a\":" + string.Concat(Enumerable.Repeat("[", 10)) + string.Concat(Enumerable.Repeat("]", 10)) + "}";
            byte[] tarball = NewPackage().File("package/SearchParameter-a.json", deep).Build();
            var options = new FhirPackageReaderOptions { MaxJsonDepth = 5 };

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader(options).ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("TooCostly", issue.Code);
            Assert.Contains("JsonDepthLimit", issue.Diagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenAHighlyCompressibleArchive_WhenTheRatioLimitIsJustBelowOrAtItsRatio_ThenItIsRejectedOrAccepted()
        {
            // Arrange
            TarballBuilder builder = NewPackage().File("package/SearchParameter-a.json", SearchParameterJson(description: new string(' ', 200_000)));
            byte[] tarball = builder.Build();
            long expanded = builder.TarLength;
            int smallestPassingRatio = checked((int)((expanded + tarball.Length - 1) / tarball.Length));

            // Act
            FhirPackage accepted = await CreateReader(new FhirPackageReaderOptions { MaxExpansionRatio = smallestPassingRatio })
                .ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None);
            FhirPackageException rejected = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader(new FhirPackageReaderOptions { MaxExpansionRatio = smallestPassingRatio - 1 })
                    .ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None));

            // Assert
            Assert.True(smallestPassingRatio > 2, $"The fixture should be highly compressible, but its ratio is only {smallestPassingRatio}.");
            Assert.Single(accepted.ResourceEntries);
            Assert.Equal("TooCostly", Assert.Single(rejected.Issues).Code);
        }

        [Theory]
        [InlineData(999, 10, 100, false)]
        [InlineData(1000, 10, 100, false)]
        [InlineData(1001, 10, 100, true)]
        [InlineData(1, 0, 100, true)]
        [InlineData(0, 0, 100, false)]
        public void GivenSizes_WhenCheckingTheExpansionRatio_ThenExactlyAtTheLimitIsAccepted(long expanded, long compressed, int ratio, bool exceeds)
        {
            Assert.Equal(exceeds, IgnixaFhirPackageReader.ExceedsExpansionRatio(expanded, compressed, ratio));
        }

        [Theory]
        [InlineData("package/../evil.json")]
        [InlineData("/package/evil.json")]
        [InlineData("package\\evil.json")]
        [InlineData("C:/package/evil.json")]
        [InlineData("evil.json")]
        public async Task GivenAnEntryWithAnUnsafePath_WhenRead_ThenItIsRejectedAsASecurityIssue(string path)
        {
            // Arrange
            byte[] tarball = NewPackage().File(path, SearchParameterJson()).Build();

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader().ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None));

            // Assert
            Assert.Equal("Security", Assert.Single(exception.Issues).Code);
        }

        [Theory]
        [InlineData(TarEntryType.SymbolicLink)]
        [InlineData(TarEntryType.HardLink)]
        public async Task GivenALinkEntry_WhenRead_ThenItIsRejectedAsASecurityIssue(TarEntryType linkType)
        {
            // Arrange
            byte[] tarball = NewPackage()
                .File("package/SearchParameter-a.json", SearchParameterJson())
                .Link(linkType, "package/link.json", "package/SearchParameter-a.json")
                .Build();

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader().ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None));

            // Assert
            Assert.Equal("Security", Assert.Single(exception.Issues).Code);
        }

        [Fact]
        public async Task GivenAnArchiveWithoutAManifest_WhenRead_ThenItIsRejectedAsInvalid()
        {
            // Arrange
            byte[] tarball = new TarballBuilder().File("package/SearchParameter-a.json", SearchParameterJson()).Build();

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader().ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("Invalid", issue.Code);
            Assert.Contains("MissingManifest", issue.Diagnostics, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("other.package", PackageVersion)]
        [InlineData(PackageId, "1.0.1")]
        public async Task GivenAManifestThatDoesNotMatchTheDescriptor_WhenRead_ThenItIsRejectedAsInvalid(string expectedId, string expectedVersion)
        {
            // Arrange
            byte[] tarball = NewPackage().File("package/SearchParameter-a.json", SearchParameterJson()).Build();

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader().ReadAsync(new MemoryStream(tarball), new FhirPackageDescriptor(expectedId, expectedVersion, Integrity(tarball)), CancellationToken.None));

            // Assert
            Assert.Equal("Invalid", Assert.Single(exception.Issues).Code);
        }

        [Fact]
        public async Task GivenNoFhirCoreDependency_WhenRead_ThenItIsRejectedAsInvalid()
        {
            // Arrange
            byte[] tarball = NewPackage(manifest => manifest.Remove("dependencies")).File("package/SearchParameter-a.json", SearchParameterJson()).Build();

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader().ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("Invalid", issue.Code);
            Assert.Contains(CorePackageId, issue.Diagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenACoreDependencyOnADifferentFhirVersion_WhenRead_ThenItIsRejectedAsNotSupported()
        {
            // Arrange
            string otherCore = CorePackageId == "hl7.fhir.r5.core" ? "hl7.fhir.r4.core" : "hl7.fhir.r5.core";
            byte[] tarball = NewPackage(manifest =>
                {
                    manifest["dependencies"] = new JsonObject { [otherCore] = "9.9.9" };
                    manifest.Remove("fhirVersions");
                })
                .File("package/SearchParameter-a.json", SearchParameterJson())
                .Build();

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader().ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("NotSupported", issue.Code);
            Assert.Contains(otherCore, issue.Diagnostics, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("0.0.1")]
        [InlineData("4.0")]
        [InlineData("latest")]
        public async Task GivenTheRightCorePackageAtAnotherVersion_WhenRead_ThenItIsRejectedAsNotSupported(string coreVersion)
        {
            // Arrange
            byte[] tarball = NewPackage(manifest =>
                {
                    manifest["dependencies"][CorePackageId] = coreVersion;
                    manifest.Remove("fhirVersions");
                })
                .File("package/SearchParameter-a.json", SearchParameterJson())
                .Build();

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader().ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None));

            // Assert
            Assert.Equal("NotSupported", Assert.Single(exception.Issues).Code);
        }

        [Fact]
        public async Task GivenASecondFhirCoreDependency_WhenRead_ThenItIsRejectedAsNotSupported()
        {
            // Arrange
            string otherCore = CorePackageId == "hl7.fhir.r5.core" ? "hl7.fhir.r4.core" : "hl7.fhir.r5.core";
            byte[] tarball = NewPackage(manifest => manifest["dependencies"][otherCore] = "9.9.9")
                .File("package/SearchParameter-a.json", SearchParameterJson())
                .Build();

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader().ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None));

            // Assert
            Assert.Equal("NotSupported", Assert.Single(exception.Issues).Code);
        }

        [Fact]
        public async Task GivenADeclaredFhirVersionThatDoesNotMatchTheServer_WhenRead_ThenItIsRejectedAsNotSupported()
        {
            // Arrange
            byte[] tarball = NewPackage(manifest => manifest["fhirVersions"] = new JsonArray(ServerFhirVersion, "9.9.9"))
                .File("package/SearchParameter-a.json", SearchParameterJson())
                .Build();

            // Act
            FhirPackageException exception = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader().ReadAsync(new MemoryStream(tarball), Descriptor(tarball), CancellationToken.None));

            // Assert
            OperationOutcomeIssue issue = Assert.Single(exception.Issues);
            Assert.Equal("NotSupported", issue.Code);
            Assert.Contains("9.9.9", issue.Diagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenAPackageBuiltWithNpmPack_WhenRead_ThenItPassesStrictExtraction()
        {
            // Arrange
            byte[] tarball = EmbeddedResourceManager.GetBytesContent("TestFiles", "FhirPackage-SearchParameters", "tgz");
            var descriptor = new FhirPackageDescriptor("example.fhir.searchparameters", "1.0.0", Integrity(tarball));

            // Act
            FhirPackage package = await CreateReader().ReadAsync(new MemoryStream(tarball), descriptor, CancellationToken.None);

            // Assert
            Assert.Equal(
                new[] { "SearchParameter", "StructureDefinition" },
                package.ResourceEntries.Select(x => x.ResourceType).OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal("package/example/Patient-example.json", Assert.Single(package.IgnoredEntries).Path);
            Assert.Empty(package.IgnoredDependencies);
        }

        private IgnixaFhirPackageReader CreateReader(FhirPackageReaderOptions options = null) =>
            new IgnixaFhirPackageReader(_modelInfoProvider, Options.Create(options ?? new FhirPackageReaderOptions()), NullLoggerFactory.Instance);

        private TarballBuilder NewPackage(Action<JsonObject> customizeManifest = null)
        {
            var manifest = new JsonObject
            {
                ["name"] = PackageId,
                ["version"] = PackageVersion,
                ["fhirVersions"] = new JsonArray(ServerFhirVersion),
                ["dependencies"] = new JsonObject { [CorePackageId] = ServerFhirVersion },
            };

            customizeManifest?.Invoke(manifest);
            return new TarballBuilder().File("package/package.json", manifest.ToJsonString());
        }

        private static string SearchParameterJson(string description = "Test search parameter.") =>
            new JsonObject
            {
                ["resourceType"] = "SearchParameter",
                ["url"] = "http://example.org/fhir/SearchParameter/a",
                ["name"] = "a",
                ["status"] = "active",
                ["description"] = description,
                ["code"] = "a",
                ["base"] = new JsonArray("Patient"),
                ["type"] = "token",
                ["expression"] = "Patient.identifier",
            }.ToJsonString();

        private static FhirPackageDescriptor Descriptor(byte[] tarball) => new FhirPackageDescriptor(PackageId, PackageVersion, Integrity(tarball));

        private static string Integrity(byte[] tarball) => "sha512-" + Convert.ToBase64String(SHA512.HashData(tarball));

        /// <summary>
        /// Builds gzip-compressed USTAR archives in memory, including deliberately unsafe entries.
        /// </summary>
        internal sealed class TarballBuilder
        {
            private readonly List<UstarTarEntry> _entries = new List<UstarTarEntry>();

            public long TarLength { get; private set; }

            public TarballBuilder File(string path, string content)
            {
                _entries.Add(new UstarTarEntry(TarEntryType.RegularFile, path) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) });
                return this;
            }

            public TarballBuilder Link(TarEntryType linkType, string path, string target)
            {
                _entries.Add(new UstarTarEntry(linkType, path) { LinkName = target });
                return this;
            }

            public byte[] Build()
            {
                using var tar = new MemoryStream();
                using (var writer = new TarWriter(tar, TarEntryFormat.Ustar, leaveOpen: true))
                {
                    foreach (UstarTarEntry entry in _entries)
                    {
                        entry.DataStream?.Seek(0, SeekOrigin.Begin);
                        writer.WriteEntry(entry);
                    }
                }

                TarLength = tar.Length;

                using var gzip = new MemoryStream();
                using (var compressor = new GZipStream(gzip, CompressionLevel.SmallestSize, leaveOpen: true))
                {
                    tar.Position = 0;
                    tar.CopyTo(compressor);
                }

                return gzip.ToArray();
            }
        }
    }
}
