// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using EnsureThat;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.FhirPackages;

namespace Microsoft.Health.Fhir.Ignixa.Features.FhirPackages
{
    /// <summary>
    /// Acquires exact FHIR packages from deployment-approved npm registries.
    /// </summary>
    public sealed class IgnixaFhirPackageAcquirer : IFhirPackageAcquirer, IDisposable
    {
        private readonly FhirPackageAcquisitionOptions _options;
        private readonly IgnixaFhirPackageReader _reader;
        private readonly PackageExtractionLimits _extractionLimits;
        private readonly TokenCredential _credential;
        private readonly NpmPackageAcquirer _acquirer;

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaFhirPackageAcquirer"/> class.
        /// </summary>
        /// <param name="options">The administrator-configured source allow-list.</param>
        /// <param name="reader">Applies FHIR package checks after strict extraction.</param>
        /// <param name="readerOptions">The extraction limits shared with the package reader.</param>
        /// <param name="credential">The deployment identity used by authenticated registries.</param>
        /// <param name="loggerFactory">Creates the package extractor logger.</param>
        public IgnixaFhirPackageAcquirer(
            IOptions<FhirPackageAcquisitionOptions> options,
            IgnixaFhirPackageReader reader,
            IOptions<FhirPackageReaderOptions> readerOptions,
            TokenCredential credential,
            ILoggerFactory loggerFactory)
            : this(options, reader, readerOptions, credential, loggerFactory, validateServerCertificate: null)
        {
        }

        internal IgnixaFhirPackageAcquirer(
            IOptions<FhirPackageAcquisitionOptions> options,
            IgnixaFhirPackageReader reader,
            IOptions<FhirPackageReaderOptions> readerOptions,
            TokenCredential credential,
            ILoggerFactory loggerFactory,
            RemoteCertificateValidationCallback validateServerCertificate)
        {
            _options = EnsureArg.IsNotNull(options?.Value, nameof(options));
            _reader = EnsureArg.IsNotNull(reader, nameof(reader));
            _extractionLimits = IgnixaFhirPackageReader.CreateExtractionLimits(EnsureArg.IsNotNull(readerOptions?.Value, nameof(readerOptions)));
            _credential = EnsureArg.IsNotNull(credential, nameof(credential));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            var extractor = new PackageExtractor(loggerFactory.CreateLogger<PackageExtractor>());
            _acquirer = new NpmPackageAcquirer(extractor, AuthenticateAsync, validateServerCertificate: validateServerCertificate);
        }

        /// <inheritdoc />
        public async Task<FhirPackage> AcquireAsync(string packageId, string version, string source, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(packageId, nameof(packageId));
            EnsureArg.IsNotNullOrWhiteSpace(version, nameof(version));
            EnsureArg.IsNotNullOrWhiteSpace(source, nameof(source));

            if (!_options.Sources.TryGetValue(source, out FhirPackageSourceOptions sourceOptions) || sourceOptions == null)
            {
                throw new FhirPackageAcquisitionException("The requested package source is not configured.", isRetryable: false);
            }

            NpmPackageIdentity identity;
            try
            {
                identity = new NpmPackageIdentity(packageId, version);
            }
            catch (ArgumentException)
            {
                throw new FhirPackageAcquisitionException("The package name or exact version is invalid.", isRetryable: false);
            }

            NpmPackageSourcePolicy policy;
            try
            {
                policy = new NpmPackageSourcePolicy(
                    source,
                    new Uri(sourceOptions.Registry, UriKind.Absolute),
                    sourceOptions.AllowedArtifactPrefixes.Select(prefix => new Uri(prefix, UriKind.Absolute)),
                    _extractionLimits,
                    maxRedirects: sourceOptions.MaxRedirects);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is UriFormatException)
            {
                throw new FhirPackageAcquisitionException("The configured package source policy is invalid.", isRetryable: false);
            }

            try
            {
                AcquiredNpmPackage acquired = await _acquirer.AcquireAsync(identity, policy, cancellationToken);
                var descriptor = new FhirPackageDescriptor(acquired.Identity.Name, acquired.Identity.Version, acquired.Integrity.Integrity);
                return _reader.Interpret(acquired.Extraction, descriptor);
            }
            catch (PackageAcquisitionException ex)
            {
                throw ToFhirPackageAcquisitionException(ex);
            }
            catch (PackageExtractionException ex)
            {
                throw IgnixaFhirPackageReader.ToFhirPackageException(ex.Diagnostic);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _acquirer.Dispose();
        }

        internal static FhirPackageAcquisitionException ToFhirPackageAcquisitionException(PackageAcquisitionException exception)
        {
            bool retryable = exception.Error is PackageAcquisitionError.TransportFailure or PackageAcquisitionError.Timeout ||
                (exception.Error == PackageAcquisitionError.HttpFailure &&
                    exception.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or
                        HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout);

            string message = exception.Error == PackageAcquisitionError.TransportFailure
                ? "Package acquisition failed due to a transport or TLS error. Check registry connectivity and certificate trust before retrying."
                : exception.Message;
            return new FhirPackageAcquisitionException(message, retryable, exception);
        }

        internal async ValueTask<AuthenticationHeaderValue> AuthenticateAsync(string source, Uri destination, CancellationToken cancellationToken)
        {
            FhirPackageSourceOptions sourceOptions = _options.Sources[source];
            if (string.IsNullOrWhiteSpace(sourceOptions.TokenScope))
            {
                return null;
            }

            var registry = new Uri(sourceOptions.Registry, UriKind.Absolute);

            // Ignixa validates destinations before this callback, but preserves their escaped wire spelling.
            var canonicalDestination = new Uri(destination.OriginalString, UriKind.Absolute);
            if (!registry.IsBaseOf(canonicalDestination))
            {
                return null;
            }

            AccessToken token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { sourceOptions.TokenScope }), cancellationToken);
            return new AuthenticationHeaderValue("Bearer", token.Token);
        }
    }
}
