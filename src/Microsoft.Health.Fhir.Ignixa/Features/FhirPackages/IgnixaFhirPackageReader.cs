// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.FhirPackages;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Ignixa.Features.FhirPackages
{
    /// <summary>
    /// Reads FHIR NPM packages with Ignixa's strict, in-memory extraction.
    /// </summary>
    /// <remarks>
    /// Package reading has no Firely equivalent, so this is meant to be registered regardless of <c>CoreFeatures.FhirSdkProvider</c>
    /// (see ADR-2607). It isn't registered for DI yet; that lands with the writer (AB#206132). No Ignixa type crosses the
    /// <see cref="IFhirPackageReader"/> seam.
    /// </remarks>
    public sealed class IgnixaFhirPackageReader : IFhirPackageReader
    {
        private const string PackageRootPrefix = "package/";
        private const string Sha512Prefix = "sha512-";

        // Not [GeneratedRegex]: the SDL analyzer build passes its own compiler Features, which stops the source generator from running.
        private static readonly Regex CorePackageIdRegex = new Regex(@"^hl7\.fhir\.r\d+b?\.core$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly FhirPackageReaderOptions _options;
        private readonly PackageExtractor _extractor;
        private readonly ILogger<IgnixaFhirPackageReader> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaFhirPackageReader"/> class.
        /// </summary>
        /// <param name="modelInfoProvider">Provides the server's FHIR version.</param>
        /// <param name="options">The reader limits.</param>
        /// <param name="loggerFactory">Creates loggers for the reader and the Ignixa extractor.</param>
        public IgnixaFhirPackageReader(IModelInfoProvider modelInfoProvider, IOptions<FhirPackageReaderOptions> options, ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(options, nameof(options));
            EnsureArg.IsNotNull(options.Value, nameof(options));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _modelInfoProvider = modelInfoProvider;
            _options = options.Value;
            _extractor = new PackageExtractor(loggerFactory.CreateLogger<PackageExtractor>());
            _logger = loggerFactory.CreateLogger<IgnixaFhirPackageReader>();

            // Fail at construction, not on first use, if the configured limits can't be honoured.
            _ = CreateExtractionLimits(_options);
            EnsureArg.IsGt(_options.MaxExpansionRatio, 0, nameof(FhirPackageReaderOptions.MaxExpansionRatio));
        }

        /// <inheritdoc />
        public async Task<FhirPackage> ReadAsync(Stream tarball, FhirPackageDescriptor descriptor, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(tarball, nameof(tarball));
            EnsureArg.IsNotNull(descriptor, nameof(descriptor));

            byte[] expectedDigest = DecodeExpectedIntegrity(descriptor.Integrity);
            byte[] compressed = await ReadBoundedAsync(tarball, _options.MaxCompressedBytes, cancellationToken);

            if (!CryptographicOperations.FixedTimeEquals(SHA512.HashData(compressed), expectedDigest))
            {
                throw new FhirPackageException(OperationOutcomeConstants.IssueType.Security, Core.Resources.FhirPackageIntegrityMismatch);
            }

            StrictPackageExtractionResult extraction;
            try
            {
                using var buffer = new MemoryStream(compressed, writable: false);
                extraction = await _extractor.ExtractStrictAsync(buffer, CreateExtractionLimits(_options), cancellationToken);
            }
            catch (PackageExtractionException ex)
            {
                throw ToFhirPackageException(ex.Diagnostic);
            }

            return Interpret(extraction, descriptor);
        }

        /// <summary>
        /// Applies the checks that follow extraction. Every input path, including the acquired-package adapter
        /// that AB#206133 adds, goes through this method so the paths can't disagree.
        /// </summary>
        /// <param name="extraction">A completed strict extraction.</param>
        /// <param name="descriptor">The package the caller expects; its integrity must already have been verified.</param>
        /// <returns>The verified package.</returns>
        internal FhirPackage Interpret(StrictPackageExtractionResult extraction, FhirPackageDescriptor descriptor)
        {
            EnsureArg.IsNotNull(extraction, nameof(extraction));
            EnsureArg.IsNotNull(descriptor, nameof(descriptor));

            PackageExtractionStatistics statistics = extraction.Statistics;
            if (ExceedsExpansionRatio(statistics.ExpandedBytes, statistics.CompressedBytes, _options.MaxExpansionRatio))
            {
                throw new FhirPackageException(
                    OperationOutcomeConstants.IssueType.TooCostly,
                    string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageExpansionRatioExceeded, statistics.ExpandedBytes, statistics.CompressedBytes, _options.MaxExpansionRatio));
            }

            StrictPackageManifest manifest = extraction.Manifest;
            if (!string.Equals(manifest.Name, descriptor.PackageId, StringComparison.Ordinal) ||
                !string.Equals(manifest.Version, descriptor.Version, StringComparison.Ordinal))
            {
                throw new FhirPackageException(
                    OperationOutcomeConstants.IssueType.Invalid,
                    string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageIdentityMismatch, manifest.Name, manifest.Version, descriptor.PackageId, descriptor.Version));
            }

            List<string> ignoredDependencies = CheckFhirVersionAndDependencies(manifest);

            var resourceEntries = new List<FhirPackageEntry>();
            var ignoredEntries = new List<FhirPackageEntry>();
            foreach (StrictPackageEntry entry in extraction.JsonEntries)
            {
                if (entry.ResourceType == null)
                {
                    // Package metadata such as .index.json; expected content, not something to install or report.
                    continue;
                }

                var packageEntry = new FhirPackageEntry(entry.Path, entry.ResourceType, entry.Json);
                if (IsAtPackageRoot(entry.Path))
                {
                    resourceEntries.Add(packageEntry);
                }
                else
                {
                    ignoredEntries.Add(packageEntry);
                }
            }

            _logger.LogInformation(
                "Read FHIR package {PackageId} version {Version}: {ResourceCount} root resource(s), {IgnoredCount} ignored resource(s), {DependencyCount} non-core dependency(ies).",
                manifest.Name,
                manifest.Version,
                resourceEntries.Count,
                ignoredEntries.Count,
                ignoredDependencies.Count);

            return new FhirPackage(descriptor, resourceEntries, ignoredEntries, ignoredDependencies);
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="expandedBytes"/> is more than <paramref name="maxRatio"/> times
        /// <paramref name="compressedBytes"/>. Uses multiplication in <see cref="long"/> arithmetic, so there is no
        /// integer-division truncation, and a ratio exactly at the limit is accepted.
        /// </summary>
        /// <param name="expandedBytes">The decompressed size.</param>
        /// <param name="compressedBytes">The compressed size.</param>
        /// <param name="maxRatio">The largest accepted ratio.</param>
        /// <returns><c>true</c> if the ratio is exceeded.</returns>
        internal static bool ExceedsExpansionRatio(long expandedBytes, long compressedBytes, int maxRatio)
        {
            if (compressedBytes <= 0)
            {
                return expandedBytes > 0;
            }

            // Both operands are bounded by int limits, so the product fits in a long.
            return expandedBytes > compressedBytes * maxRatio;
        }

        private static PackageExtractionLimits CreateExtractionLimits(FhirPackageReaderOptions options)
        {
            try
            {
                return new PackageExtractionLimits(
                    maxCompressedBytes: options.MaxCompressedBytes,
                    maxExpandedBytes: options.MaxExpandedBytes,
                    maxEntryBytes: options.MaxEntryBytes,
                    maxArchiveEntries: options.MaxEntries,
                    maxJsonDepth: options.MaxJsonDepth);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new ArgumentException($"Invalid {nameof(FhirPackageReaderOptions)}: every limit must be a positive value.", nameof(options), ex);
            }
        }

        private static byte[] DecodeExpectedIntegrity(string integrity)
        {
            // NpmPackageIntegrity enforces exactly one canonical sha512 SRI token; anything else, including sha1, is rejected.
            try
            {
                var parsed = new NpmPackageIntegrity(integrity);
                return Convert.FromBase64String(parsed.Integrity.Substring(Sha512Prefix.Length));
            }
            catch (ArgumentException)
            {
                throw new FhirPackageException(OperationOutcomeConstants.IssueType.Security, Core.Resources.FhirPackageIntegrityInvalid);
            }
        }

        private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw new FhirPackageException(
                        OperationOutcomeConstants.IssueType.TooCostly,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageCompressedSizeExceeded, maxBytes));
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }

            return buffer.ToArray();
        }

        private static bool IsAtPackageRoot(string path) =>
            path.StartsWith(PackageRootPrefix, StringComparison.Ordinal) &&
            path.IndexOf('/', PackageRootPrefix.Length, StringComparison.Ordinal) < 0;

        private static FhirPackageException ToFhirPackageException(PackageExtractionDiagnostic diagnostic)
        {
            string issueType = diagnostic.Code switch
            {
                PackageExtractionError.CompressedSizeLimit or
                PackageExtractionError.ExpandedSizeLimit or
                PackageExtractionError.EntrySizeLimit or
                PackageExtractionError.EntryCountLimit or
                PackageExtractionError.JsonDepthLimit => OperationOutcomeConstants.IssueType.TooCostly,
                PackageExtractionError.UnsafePath or
                PackageExtractionError.UnsupportedEntryType => OperationOutcomeConstants.IssueType.Security,
                _ => OperationOutcomeConstants.IssueType.Invalid,
            };

            string detail = diagnostic.Code.ToString();
            if (diagnostic.EntryIndex.HasValue)
            {
                detail += string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageArchiveEntryDetail, diagnostic.EntryIndex.Value);
            }

            if (diagnostic.Limit.HasValue)
            {
                detail += string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageArchiveLimitDetail, diagnostic.Limit.Value);
            }

            return new FhirPackageException(issueType, string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageArchiveRejected, detail));
        }

        private static Dictionary<string, string> ReadDependencies(string manifestJson, int maxJsonDepth)
        {
            var dependencies = new Dictionary<string, string>(StringComparer.Ordinal);

            // Use the same depth limit strict extraction already enforced; the default (64) would reject manifests it accepted.
            using JsonDocument document = JsonDocument.Parse(manifestJson, new JsonDocumentOptions { MaxDepth = maxJsonDepth });
            if (!document.RootElement.TryGetProperty("dependencies", out JsonElement element))
            {
                return dependencies;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new FhirPackageException(
                    OperationOutcomeConstants.IssueType.Invalid,
                    string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageManifestInvalid, "'dependencies' must be an object."));
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new FhirPackageException(
                        OperationOutcomeConstants.IssueType.Invalid,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageManifestInvalid, $"the version of dependency '{property.Name}' must be a string."));
                }

                dependencies[property.Name] = property.Value.GetString();
            }

            return dependencies;
        }

        private static string GetCorePackageId(FhirSpecification version) => version switch
        {
            FhirSpecification.Stu3 => "hl7.fhir.r3.core",
            FhirSpecification.R4 => "hl7.fhir.r4.core",
            FhirSpecification.R4B => "hl7.fhir.r4b.core",
            FhirSpecification.R5 => "hl7.fhir.r5.core",
            _ => throw new NotSupportedException($"FHIR version '{version}' is not supported for packages."),
        };

        private static bool IsCorePackageId(string packageId) => CorePackageIdRegex.IsMatch(packageId);

        private List<string> CheckFhirVersionAndDependencies(StrictPackageManifest manifest)
        {
            string serverVersion = _modelInfoProvider.SupportedVersion.VersionString;
            string corePackage = GetCorePackageId(_modelInfoProvider.Version);

            foreach (string declared in manifest.FhirVersions.Append(manifest.FhirVersion).Where(x => x != null))
            {
                if (!string.Equals(declared, serverVersion, StringComparison.Ordinal))
                {
                    throw new FhirPackageException(
                        OperationOutcomeConstants.IssueType.NotSupported,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageFhirVersionMismatch, declared, serverVersion));
                }
            }

            Dictionary<string, string> dependencies = ReadDependencies(manifest.Json, _options.MaxJsonDepth);

            if (!dependencies.TryGetValue(corePackage, out string coreVersion))
            {
                string otherCore = dependencies.Keys.FirstOrDefault(IsCorePackageId);
                if (otherCore != null)
                {
                    throw new FhirPackageException(
                        OperationOutcomeConstants.IssueType.NotSupported,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageCoreDependencyMismatch, otherCore, dependencies[otherCore], corePackage, serverVersion));
                }

                throw new FhirPackageException(
                    OperationOutcomeConstants.IssueType.Invalid,
                    string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageCoreDependencyMissing, corePackage, serverVersion));
            }

            if (!string.Equals(coreVersion, serverVersion, StringComparison.Ordinal))
            {
                throw new FhirPackageException(
                    OperationOutcomeConstants.IssueType.NotSupported,
                    string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageCoreDependencyMismatch, corePackage, coreVersion, corePackage, serverVersion));
            }

            var ignored = new List<string>();
            foreach (KeyValuePair<string, string> dependency in dependencies.Where(x => !string.Equals(x.Key, corePackage, StringComparison.Ordinal)).OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (IsCorePackageId(dependency.Key))
                {
                    // A second FHIR core dependency means the package targets more than one FHIR version.
                    throw new FhirPackageException(
                        OperationOutcomeConstants.IssueType.NotSupported,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageCoreDependencyMismatch, dependency.Key, dependency.Value, corePackage, serverVersion));
                }

                ignored.Add($"{dependency.Key}@{dependency.Value}");
            }

            return ignored;
        }
    }
}
