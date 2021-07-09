// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text.RegularExpressions;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class WeakETag
    {
        private const string WeakEtagPattern = @"^W\/\""(.+)\""$";

        /// <summary>
        /// Initializes a new instance of the <see cref="WeakETag"/> class.
        /// This class handles the conversion of a version to a weak etag and parsing of a weak etag to a version
        /// </summary>
        /// <param name="versionId">The version to do the weak etag operations on</param>
        private WeakETag(string versionId)
        {
            VersionId = versionId;
        }

        /// <summary>
        /// Gets the base versionId
        /// </summary>
        /// <returns>WeakETag without the weak etag decoration</returns>
        public string VersionId { get; }

        /// <summary>
        /// Create a WeakETag with the supplied version id
        /// </summary>
        /// <param name="versionId">The version contained within the weak ETag</param>
        /// <returns>An instance of the WeakETag class</returns>
        public static WeakETag FromVersionId(string versionId)
        {
            EnsureArg.IsNotNull(versionId, nameof(versionId));

            var match = Regex.Match(versionId, WeakEtagPattern);

            if (match.Success)
            {
                // This throws an argument exception because it should only be caused by internal calls
                throw new ArgumentException(Core.Resources.VersionIdFormatNotETag, nameof(versionId));
            }

            return new WeakETag(versionId);
        }

        /// <summary>
        /// Create a WeakETag with the supplied weak ETag string
        /// </summary>
        /// <param name="weakETag">The weak ETag value to use</param>
        /// <returns>An instance of the WeakETag class</returns>
        public static WeakETag FromWeakETag(string weakETag)
        {
            EnsureArg.IsNotNull(weakETag, nameof(weakETag));

            var match = Regex.Match(weakETag, WeakEtagPattern);

            if (!match.Success)
            {
                // This throws a bad request exception because it was potentially supplied by an end user
                throw new BadRequestException(Core.Resources.WeakETagFormatRequired);
            }

            return new WeakETag(match.Groups[1].Value);
        }

        /// <summary>
        /// Returns the weak etag
        /// </summary>
        /// <returns>WeakETag with the weak etag decoration</returns>
        public override string ToString()
        {
            return $"W/\"{VersionId}\"";
        }
    }
}
