// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.FhirPackages;
using Microsoft.Health.Fhir.Ignixa.Features.FhirPackages;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.FhirPackages
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.CustomSearch)]
    public class IgnixaFhirPackageAcquirerTests
    {
        private const string Registry = "https://registry.example.test/npm/";
        private const string ArtifactPrefix = "https://artifacts.example.test/packages/";
        private const string TokenScope = "https://registry.example.test/.default";

        [Theory]
        [InlineData("https://registry.example.test/npm/package", true)]
        [InlineData("https://registry.example.test/npm/nested/package", true)]
        [InlineData("https://registry.example.test/npm-other/package", false)]
        [InlineData("https://registry.example.test/other/package", false)]
        [InlineData("https://registry.example.test:444/npm/package", false)]
        [InlineData("https://artifacts.example.test/packages/package", false)]
        [InlineData("http://registry.example.test/npm/package", false)]
        public async Task GivenARegistryCredential_WhenAuthenticating_ThenItIsSentOnlyWithinTheRegistryDirectory(string destination, bool authenticated)
        {
            // Arrange
            var credential = new RecordingCredential();
            using var acquirer = CreateAcquirer(OptionsFor(Registry, ArtifactPrefix, TokenScope), credential);

            // Act
            AuthenticationHeaderValue header = await acquirer.AuthenticateAsync("trusted", new Uri(destination), CancellationToken.None);

            // Assert
            Assert.Equal(authenticated ? 1 : 0, credential.Calls);
            if (authenticated)
            {
                Assert.Equal("Bearer", header.Scheme);
                Assert.Equal("test-token", header.Parameter);
                Assert.Equal(TokenScope, Assert.Single(credential.LastScope));
            }
            else
            {
                Assert.Null(header);
            }
        }

        [Fact]
        public async Task GivenAnUnauthenticatedRegistry_WhenAuthenticating_ThenNoCredentialIsRequested()
        {
            // Arrange
            var credential = new RecordingCredential();
            using var acquirer = CreateAcquirer(OptionsFor(Registry, ArtifactPrefix, tokenScope: null), credential);

            // Act
            AuthenticationHeaderValue header = await acquirer.AuthenticateAsync("trusted", new Uri(Registry + "package"), CancellationToken.None);

            // Assert
            Assert.Null(header);
            Assert.Equal(0, credential.Calls);
        }

        [Theory]
        [InlineData("https://registry.example.test/np%6D/package?sig=a%2Bb", true)]
        [InlineData("https://registry.example.test/npm-other/package?sig=a%2Bb", false)]
        public async Task GivenAPreservedWireUri_WhenAuthenticating_ThenTheCanonicalRegistryDirectoryIsUsed(string destination, bool authenticated)
        {
            // Arrange
            var credential = new RecordingCredential();
            using var acquirer = CreateAcquirer(OptionsFor(Registry, ArtifactPrefix, TokenScope), credential);
            var uri = new Uri(destination, new UriCreationOptions { DangerousDisablePathAndQueryCanonicalization = true });

            // Act
            AuthenticationHeaderValue header = await acquirer.AuthenticateAsync("trusted", uri, CancellationToken.None);

            // Assert
            Assert.Equal(authenticated ? 1 : 0, credential.Calls);
            Assert.Equal(authenticated, header != null);
        }

        [Theory]
        [InlineData("http://registry.example.test/npm/", ArtifactPrefix)]
        [InlineData("https://registry.example.test/npm", ArtifactPrefix)]
        [InlineData(Registry, "http://artifacts.example.test/packages/")]
        [InlineData(Registry, "https://artifacts.example.test/packages")]
        public async Task GivenAnInvalidSourcePolicy_WhenAcquiring_ThenItFailsBeforeAnyRequest(string registry, string artifactPrefix)
        {
            // Arrange
            var credential = new RecordingCredential();
            using var acquirer = CreateAcquirer(OptionsFor(registry, artifactPrefix, TokenScope), credential);

            // Act
            FhirPackageAcquisitionException exception = await Assert.ThrowsAsync<FhirPackageAcquisitionException>(
                () => acquirer.AcquireAsync("example.package", "1.0.0", "trusted", CancellationToken.None));

            // Assert
            Assert.False(exception.IsRetryable);
            Assert.Contains("source policy is invalid", exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, credential.Calls);
        }

        [Theory]
        [InlineData("Example.Package", "1.0.0")]
        [InlineData("example.package", "latest")]
        [InlineData("example.package", "1.0")]
        public async Task GivenAnInvalidPackageIdentity_WhenAcquiring_ThenItFailsBeforeAnyRequest(string packageId, string version)
        {
            // Arrange
            var credential = new RecordingCredential();
            using var acquirer = CreateAcquirer(OptionsFor(Registry, ArtifactPrefix, TokenScope), credential);

            // Act
            FhirPackageAcquisitionException exception = await Assert.ThrowsAsync<FhirPackageAcquisitionException>(
                () => acquirer.AcquireAsync(packageId, version, "trusted", CancellationToken.None));

            // Assert
            Assert.False(exception.IsRetryable);
            Assert.Contains("exact version is invalid", exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, credential.Calls);
        }

        [Fact]
        public async Task GivenAnUnconfiguredSource_WhenAcquiring_ThenItFailsBeforeAnyRequest()
        {
            // Arrange
            var credential = new RecordingCredential();
            using var acquirer = CreateAcquirer(OptionsFor(Registry, ArtifactPrefix, TokenScope), credential);

            // Act
            FhirPackageAcquisitionException exception = await Assert.ThrowsAsync<FhirPackageAcquisitionException>(
                () => acquirer.AcquireAsync("example.package", "1.0.0", "unknown", CancellationToken.None));

            // Assert
            Assert.False(exception.IsRetryable);
            Assert.Contains("not configured", exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, credential.Calls);
        }

        [Theory]
        [InlineData(HttpStatusCode.RequestTimeout, true)]
        [InlineData(HttpStatusCode.TooManyRequests, true)]
        [InlineData(HttpStatusCode.InternalServerError, true)]
        [InlineData(HttpStatusCode.BadGateway, true)]
        [InlineData(HttpStatusCode.ServiceUnavailable, true)]
        [InlineData(HttpStatusCode.GatewayTimeout, true)]
        [InlineData(HttpStatusCode.NotImplemented, false)]
        [InlineData(HttpStatusCode.HttpVersionNotSupported, false)]
        [InlineData(HttpStatusCode.Forbidden, false)]
        public void GivenAnHttpAcquisitionFailure_WhenMappingIt_ThenOnlySupportedTransientStatusesAreRetryable(HttpStatusCode status, bool retryable)
        {
            // Arrange
            var failure = new PackageAcquisitionException(PackageAcquisitionError.HttpFailure, status);

            // Act
            FhirPackageAcquisitionException result = IgnixaFhirPackageAcquirer.ToFhirPackageAcquisitionException(failure);

            // Assert
            Assert.Equal(retryable, result.IsRetryable);
            Assert.Same(failure, result.InnerException);
        }

        [Fact]
        public void GivenAnAmbiguousTransportFailure_WhenMappingIt_ThenTheDiagnosticIncludesTlsGuidance()
        {
            // Arrange
            var failure = new PackageAcquisitionException(PackageAcquisitionError.TransportFailure);

            // Act
            FhirPackageAcquisitionException result = IgnixaFhirPackageAcquirer.ToFhirPackageAcquisitionException(failure);

            // Assert
            Assert.True(result.IsRetryable);
            Assert.Contains("certificate trust", result.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(PackageAcquisitionError.Timeout, true)]
        [InlineData(PackageAcquisitionError.AuthenticationFailure, false)]
        [InlineData(PackageAcquisitionError.DigestMismatch, false)]
        [InlineData(PackageAcquisitionError.CacheFailure, false)]
        public void GivenANonHttpAcquisitionFailure_WhenMappingIt_ThenItsRetryabilityIsClassified(PackageAcquisitionError error, bool retryable)
        {
            // Arrange
            var failure = new PackageAcquisitionException(error);

            // Act
            FhirPackageAcquisitionException result = IgnixaFhirPackageAcquirer.ToFhirPackageAcquisitionException(failure);

            // Assert
            Assert.Equal(retryable, result.IsRetryable);
        }

        private static FhirPackageAcquisitionOptions OptionsFor(string registry, string artifactPrefix, string tokenScope)
        {
            var source = new FhirPackageSourceOptions { Registry = registry, TokenScope = tokenScope };
            source.AllowedArtifactPrefixes.Add(artifactPrefix);
            var options = new FhirPackageAcquisitionOptions();
            options.Sources.Add("trusted", source);
            return options;
        }

        private static IgnixaFhirPackageAcquirer CreateAcquirer(FhirPackageAcquisitionOptions options, TokenCredential credential)
        {
            var readerOptions = Options.Create(new FhirPackageReaderOptions());
            var reader = new IgnixaFhirPackageReader(new VersionSpecificModelInfoProvider(), readerOptions, NullLoggerFactory.Instance);
            return new IgnixaFhirPackageAcquirer(Options.Create(options), reader, readerOptions, credential, NullLoggerFactory.Instance);
        }

        private sealed class RecordingCredential : TokenCredential
        {
            public int Calls { get; private set; }

            public string[] LastScope { get; private set; }

            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                Calls++;
                LastScope = requestContext.Scopes;
                return ValueTask.FromResult(new AccessToken("test-token", DateTimeOffset.UtcNow.AddMinutes(5)));
            }
        }
    }
}
