// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Microsoft.Health.Fhir.SqlServer.Features.Partitions
{
    /// <summary>
    /// Configures the partitions available when the server starts.
    /// </summary>
    public class DatabasePartitioningOptions : IValidatableObject
    {
        /// <summary>
        /// Gets the partitions. Changes require restarting the server.
        /// </summary>
        public IReadOnlyDictionary<string, PartitionOptions> Partitions { get; init; } = FrozenDictionary<string, PartitionOptions>.Empty;

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Partitions is not null)
            {
                foreach ((string id, PartitionOptions partition) in Partitions)
                {
                    if (string.IsNullOrWhiteSpace(id) || id is "." or ".." || !id.All(IsUnreserved))
                    {
                        yield return new ValidationResult("Id must contain only ASCII letters, digits, '-', '_', '.', or '~', other than '.' or '..'.", [nameof(Partitions)]);
                    }

                    if (string.IsNullOrWhiteSpace(partition?.Database))
                    {
                        yield return new ValidationResult($"Missing database for partition '{id}'.", [nameof(Partitions)]);
                    }
                }
            }
        }

        private static bool IsUnreserved(char value) =>
            char.IsAsciiLetterOrDigit(value) || value is '-' or '_' or '.' or '~';
    }
}
