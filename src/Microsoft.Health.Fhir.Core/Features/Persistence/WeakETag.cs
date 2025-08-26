// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http.Headers;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class WeakETag
    {
        private readonly EntityTagHeaderValue _entityTag;

        /// <summary>
        /// Initializes a new instance of the <see cref="WeakETag"/> class.
        /// This class handles the conversion of a version to a weak etag and parsing of a weak etag to a version
        /// </summary>
        /// <param name="versionId">The version to do the weak etag operations on</param>
        private WeakETag(string versionId)
        {
            VersionId = versionId;
            // Create a weak ETag using EntityTagHeaderValue
            // Need to add quotes if not already present
            string tagValue = versionId.StartsWith("\"") && versionId.EndsWith("\"") ? versionId : $"\"{versionId}\"";
            _entityTag = new EntityTagHeaderValue(tagValue, isWeak: true);
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

            // Check if the versionId is already formatted as a weak ETag
            if (EntityTagHeaderValue.TryParse(versionId, out var parsed) && parsed.IsWeak)
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

            if (!EntityTagHeaderValue.TryParse(weakETag, out var parsed) || !parsed.IsWeak)
            {
                // This throws a bad request exception because it was potentially supplied by an end user
                throw new BadRequestException(string.Format(Core.Resources.WeakETagFormatRequired, weakETag));
            }

            // Extract the version ID from the parsed ETag
            // parsed.Tag includes quotes, so remove them
            string tag = parsed.Tag;
            string versionId = tag.StartsWith("\"") && tag.EndsWith("\"") 
                ? tag.Substring(1, tag.Length - 2) 
                : tag;

            return new WeakETag(versionId);
        }

        /// <summary>
        /// Returns the weak etag
        /// </summary>
        /// <returns>WeakETag with the weak etag decoration</returns>
        public override string ToString()
        {
            return _entityTag.ToString();
        }
    }
}
