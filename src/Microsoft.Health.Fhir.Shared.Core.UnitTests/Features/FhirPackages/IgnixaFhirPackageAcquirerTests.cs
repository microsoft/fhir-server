// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
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
        private const string PackageId = "example.fhir.searchparameters";
        private const string PackageVersion = "1.0.0";

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

        [Fact]
        public async Task GivenAValidArchive_WhenAcquired_ThenItMatchesTheReaderResult()
        {
            // Arrange
            byte[] tarball = EmbeddedResourceManager.GetBytesContent("TestFiles", "FhirPackage-SearchParameters", "tgz");
            string integrity = Integrity(tarball);
            SyntheticHttpsServer server = null;
            await using var serverLifetime = server = new SyntheticHttpsServer(path =>
            {
                if (path == $"/npm/{PackageId}/{PackageVersion}")
                {
                    return MetadataResponse(server.Url("/npm/artifact/package.tgz"), integrity);
                }

                return path == "/npm/artifact/package.tgz"
                    ? BinaryResponse(tarball)
                    : NotFoundResponse();
            });
            var readerOptions = new FhirPackageReaderOptions();
            using var expectedStream = new MemoryStream(tarball);
            FhirPackage expected = await CreateReader(readerOptions).ReadAsync(expectedStream, Descriptor(integrity), CancellationToken.None);
            var credential = new RecordingCredential();
            using var acquirer = CreateAcquirer(
                OptionsFor(server),
                credential,
                readerOptions,
                AcceptTestCertificate(server));

            // Act
            FhirPackage actual = await acquirer.AcquireAsync(PackageId, PackageVersion, "trusted", CancellationToken.None);

            // Assert
            Assert.Equal(expected.Descriptor.PackageId, actual.Descriptor.PackageId);
            Assert.Equal(expected.Descriptor.Version, actual.Descriptor.Version);
            Assert.Equal(expected.Descriptor.Integrity, actual.Descriptor.Integrity);
            Assert.Equal(expected.ResourceEntries.Select(x => (x.Path, x.ResourceType)), actual.ResourceEntries.Select(x => (x.Path, x.ResourceType)));
            Assert.Equal(expected.IgnoredEntries.Select(x => x.Path), actual.IgnoredEntries.Select(x => x.Path));
            Assert.Equal(expected.IgnoredDependencies, actual.IgnoredDependencies);
        }

        [Fact]
        public async Task GivenAnArchiveRejectedByReaderPolicy_WhenAcquired_ThenItMatchesTheReadAsyncRejection()
        {
            // Arrange
            byte[] tarball = EmbeddedResourceManager.GetBytesContent("TestFiles", "FhirPackage-SearchParameters", "tgz");
            string integrity = Integrity(tarball);
            await using var server = new SyntheticHttpsServer(path =>
            {
                if (path == $"/npm/{PackageId}/{PackageVersion}")
                {
                    return MetadataResponse(server.Url("/npm/artifact/package.tgz"), integrity);
                }

                return path == "/npm/artifact/package.tgz"
                    ? BinaryResponse(tarball)
                    : NotFoundResponse();
            });
            var readerOptions = ReaderOptionsWithExpansionRatio(1);
            using var stream = new MemoryStream(tarball);
            FhirPackageException expected = await Assert.ThrowsAsync<FhirPackageException>(
                () => CreateReader(readerOptions).ReadAsync(stream, Descriptor(integrity), CancellationToken.None));
            var credential = new RecordingCredential();
            using var acquirer = CreateAcquirer(
                OptionsFor(server),
                credential,
                readerOptions,
                AcceptTestCertificate(server));

            // Act
            FhirPackageException actual = await Assert.ThrowsAsync<FhirPackageException>(
                () => acquirer.AcquireAsync(PackageId, PackageVersion, "trusted", CancellationToken.None));

            // Assert
            Assert.Equal(Assert.Single(expected.Issues).Code, Assert.Single(actual.Issues).Code);
            Assert.Equal(Assert.Single(expected.Issues).Diagnostics, Assert.Single(actual.Issues).Diagnostics);
        }

        [Fact]
        public async Task GivenATamperedTarball_WhenAcquired_ThenItsDigestMismatchIsReported()
        {
            // Arrange
            byte[] tarball = EmbeddedResourceManager.GetBytesContent("TestFiles", "FhirPackage-SearchParameters", "tgz");
            byte[] tamperedTarball = tarball.ToArray();
            tamperedTarball[tamperedTarball.Length / 2] ^= 0x01;
            string integrity = Integrity(tarball);
            await using var server = new SyntheticHttpsServer(path =>
            {
                if (path == $"/npm/{PackageId}/{PackageVersion}")
                {
                    return MetadataResponse(server.Url("/npm/artifact/package.tgz"), integrity);
                }

                return path == "/npm/artifact/package.tgz"
                    ? BinaryResponse(tamperedTarball)
                    : NotFoundResponse();
            });
            var credential = new RecordingCredential();
            using var acquirer = CreateAcquirer(
                OptionsFor(server),
                credential,
                new FhirPackageReaderOptions(),
                AcceptTestCertificate(server));

            // Act
            FhirPackageAcquisitionException exception = await Assert.ThrowsAsync<FhirPackageAcquisitionException>(
                () => acquirer.AcquireAsync(PackageId, PackageVersion, "trusted", CancellationToken.None));

            // Assert
            Assert.False(exception.IsRetryable);
            Assert.Equal(PackageAcquisitionError.DigestMismatch, Assert.IsType<PackageAcquisitionException>(exception.InnerException).Error);
        }

        [Fact]
        public async Task GivenAnApprovedCrossHostArtifactRedirect_WhenAcquired_ThenRegistryCredentialIsNotForwarded()
        {
            // Arrange
            byte[] tarball = EmbeddedResourceManager.GetBytesContent("TestFiles", "FhirPackage-SearchParameters", "tgz");
            string integrity = Integrity(tarball);
            await using var server = new SyntheticHttpsServer(path =>
            {
                if (path == $"/npm/{PackageId}/{PackageVersion}")
                {
                    return MetadataResponse(server.Url("/npm/artifact/redirect.tgz"), integrity);
                }

                if (path == "/npm/artifact/redirect.tgz")
                {
                    return new SyntheticResponse(
                        HttpStatusCode.Found,
                        "application/octet-stream",
                        Array.Empty<byte>(),
                        server.ArtifactUrl("/packages/package.tgz"));
                }

                return path == "/packages/package.tgz"
                    ? BinaryResponse(tarball)
                    : NotFoundResponse();
            });
            var credential = new RecordingCredential();
            using var acquirer = CreateAcquirer(
                OptionsFor(server, allowRedirect: true),
                credential,
                new FhirPackageReaderOptions(),
                AcceptTestCertificate(server));

            // Act
            FhirPackage package = await acquirer.AcquireAsync(PackageId, PackageVersion, "trusted", CancellationToken.None);

            // Assert
            Assert.Equal(new[] { "SearchParameter", "StructureDefinition" }, package.ResourceEntries.Select(entry => entry.ResourceType).OrderBy(type => type, StringComparer.Ordinal));
            Assert.Equal(2, credential.Calls);
            Assert.Contains(server.Requests, request =>
                request.Path == "/npm/artifact/redirect.tgz" && request.Authorization == credential.AuthorizationHeader);
            Assert.Null(Assert.Single(server.Requests.Where(request => request.Path == "/packages/package.tgz")).Authorization);
        }

        private static FhirPackageAcquisitionOptions OptionsFor(string registry, string artifactPrefix, string tokenScope)
        {
            var source = new FhirPackageSourceOptions { Registry = registry, TokenScope = tokenScope };
            source.AllowedArtifactPrefixes.Add(artifactPrefix);
            var options = new FhirPackageAcquisitionOptions();
            options.Sources.Add("trusted", source);
            return options;
        }

        private static FhirPackageReaderOptions ReaderOptionsWithExpansionRatio(int maxExpansionRatio) =>
            new FhirPackageReaderOptions { MaxExpansionRatio = maxExpansionRatio };

        private static FhirPackageDescriptor Descriptor(string integrity) => new FhirPackageDescriptor(PackageId, PackageVersion, integrity);

        private static string Integrity(byte[] tarball) => "sha512-" + Convert.ToBase64String(SHA512.HashData(tarball));

        private static SyntheticResponse BinaryResponse(byte[] tarball) =>
            new SyntheticResponse(HttpStatusCode.OK, "application/octet-stream", tarball);

        private static SyntheticResponse NotFoundResponse() =>
            new SyntheticResponse(HttpStatusCode.NotFound, "text/plain", Array.Empty<byte>());

        private static RemoteCertificateValidationCallback AcceptTestCertificate(SyntheticHttpsServer server) =>
            (_, certificate, _, _) => certificate?.GetRawCertData().SequenceEqual(server.CertificateRawData) == true;

        private static IgnixaFhirPackageAcquirer CreateAcquirer(FhirPackageAcquisitionOptions options, TokenCredential credential)
        {
            var readerOptions = Options.Create(new FhirPackageReaderOptions());
            var reader = new IgnixaFhirPackageReader(new VersionSpecificModelInfoProvider(), readerOptions, NullLoggerFactory.Instance);
            return new IgnixaFhirPackageAcquirer(Options.Create(options), reader, readerOptions, credential, NullLoggerFactory.Instance);
        }

        private static IgnixaFhirPackageAcquirer CreateAcquirer(
            FhirPackageAcquisitionOptions options,
            TokenCredential credential,
            FhirPackageReaderOptions readerOptions,
            RemoteCertificateValidationCallback validateServerCertificate)
        {
            var configuredReaderOptions = Options.Create(readerOptions);
            var reader = new IgnixaFhirPackageReader(new VersionSpecificModelInfoProvider(), configuredReaderOptions, NullLoggerFactory.Instance);
            return new IgnixaFhirPackageAcquirer(
                Options.Create(options),
                reader,
                configuredReaderOptions,
                credential,
                NullLoggerFactory.Instance,
                validateServerCertificate);
        }

        private static IgnixaFhirPackageReader CreateReader(FhirPackageReaderOptions readerOptions) =>
            new IgnixaFhirPackageReader(new VersionSpecificModelInfoProvider(), Options.Create(readerOptions), NullLoggerFactory.Instance);

        private static FhirPackageAcquisitionOptions OptionsFor(SyntheticHttpsServer server, bool allowRedirect = false)
        {
            var source = new FhirPackageSourceOptions
            {
                Registry = server.RegistryBaseUri,
                TokenScope = TokenScope,
                MaxRedirects = allowRedirect ? 1 : 0,
            };
            source.AllowedArtifactPrefixes.Add(server.RegistryBaseUri);
            if (allowRedirect)
            {
                source.AllowedArtifactPrefixes.Add(server.ArtifactBaseUri);
            }

            var options = new FhirPackageAcquisitionOptions();
            options.Sources.Add("trusted", source);
            return options;
        }

        private static SyntheticResponse MetadataResponse(string tarballUrl, string integrity) =>
            new SyntheticResponse(
                HttpStatusCode.OK,
                "application/json",
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    name = PackageId,
                    version = PackageVersion,
                    dist = new { tarball = tarballUrl, integrity },
                }));

        private sealed record SyntheticRequest(string Path, string Authorization);

        private sealed record SyntheticResponse(HttpStatusCode StatusCode, string ContentType, byte[] Body, string Location = null);

        private sealed class SyntheticHttpsServer : IAsyncDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly X509Certificate2 _certificate = CreateCertificate();
            private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
            private readonly Func<string, SyntheticResponse> _respond;
            private readonly Task _serveTask;
            private readonly ConcurrentQueue<SyntheticRequest> _requests = new ConcurrentQueue<SyntheticRequest>();

            public SyntheticHttpsServer(Func<string, SyntheticResponse> respond)
            {
                _respond = respond;
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _serveTask = RunAsync();
            }

            public int Port { get; }

            public string RegistryBaseUri => $"https://localhost:{Port}/npm/";

            public string ArtifactBaseUri => $"https://127.0.0.1:{Port}/packages/";

            public IReadOnlyCollection<SyntheticRequest> Requests => _requests.ToArray();

            public byte[] CertificateRawData => _certificate.RawData;

            public string Url(string path) => $"https://localhost:{Port}{path}";

            public string ArtifactUrl(string path) => $"https://127.0.0.1:{Port}{path}";

            public async ValueTask DisposeAsync()
            {
                _shutdown.Cancel();
                _listener.Stop();
                try
                {
                    await _serveTask;
                }
                catch (SocketException) when (_shutdown.IsCancellationRequested)
                {
                }

                _certificate.Dispose();
                _shutdown.Dispose();
            }

            private static X509Certificate2 CreateCertificate()
            {
                using RSA key = RSA.Create(2048);
                var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            }

            private async Task RunAsync()
            {
                try
                {
                    while (!_shutdown.IsCancellationRequested)
                    {
                        using TcpClient client = await _listener.AcceptTcpClientAsync(_shutdown.Token);
                        await RespondAsync(client, _shutdown.Token);
                    }
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                {
                }
                catch (SocketException) when (_shutdown.IsCancellationRequested)
                {
                }
            }

            private async Task RespondAsync(TcpClient client, CancellationToken cancellationToken)
            {
                await using var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                await stream.AuthenticateAsServerAsync(
                    _certificate,
                    clientCertificateRequired: false,
                    enabledSslProtocols: System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                    checkCertificateRevocation: false);

                using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                string requestLine = await reader.ReadLineAsync(cancellationToken);
                if (requestLine == null)
                {
                    return;
                }

                string[] requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string header;
                while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(cancellationToken)))
                {
                    int separator = header.IndexOf(':');
                    if (separator > 0)
                    {
                        headers[header[..separator]] = header[(separator + 1)..].Trim();
                    }
                }

                string path = new Uri("https://localhost" + requestParts[1]).AbsolutePath;
                headers.TryGetValue("Authorization", out string authorization);
                _requests.Enqueue(new SyntheticRequest(path, authorization));
                SyntheticResponse response = _respond(path);
                string reason = response.StatusCode == HttpStatusCode.Found ? "Found" : "OK";
                string responseHeaders = $"HTTP/1.1 {(int)response.StatusCode} {reason}\r\n" +
                    $"Content-Type: {response.ContentType}\r\n" +
                    $"Content-Length: {response.Body.Length}\r\n" +
                    "Connection: close\r\n" +
                    (response.Location == null ? string.Empty : $"Location: {response.Location}\r\n") +
                    "\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(responseHeaders).AsMemory(), cancellationToken);
                await stream.WriteAsync(response.Body.AsMemory(), cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
        }

        private sealed class RecordingCredential : TokenCredential
        {
            public string AuthorizationHeader => new AuthenticationHeaderValue("Bearer", "test-token").ToString();

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
