// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Linq;
using static Hl7.Fhir.Model.Parameters;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Operations
{
    /// <summary>
    /// An object to represent the operation type and parameters for a FHIR
    /// Path Patch operation to be completed.
    /// </summary>
    internal class PendingOperation
    {
        /// <summary>
        /// Gets or sets the enumeration value for this instances operation type.
        /// </summary>
        internal PatchOperationType Type { get; set; }

        /// <summary>
        /// Gets or sets the parameter component of the pending operation.
        /// </summary>
        internal ParameterComponent Parameter { get; set; }

        /// <summary>
        /// Gets or sets the operation index.
        /// </summary>
        internal int? Index { get; set; }

        /// <summary>
        /// Gets or sets the source index.
        /// </summary>
        internal int? Source { get; set; }

        /// <summary>
        /// Gets or sets the destination index.
        /// </summary>
        internal int? Destination { get; set; }

        /// <summary>
        /// Gets or sets the operation name.
        /// </summary>
        internal string Name { get; set; }

        /// <summary>
        /// Gets or sets the operation path.
        /// </summary>
        internal string Path { get; set; }

        /// <summary>
        /// Gets or sets the operation value as the input ParameterComponent.
        /// </summary>
        internal ParameterComponent Value { get; set; }

        /// <summary>
        /// Convert from ParameterComponent to PendingOperation Object.
        /// </summary>
        /// <param name="component">ParameterComponent.</param>
        /// <returns>PendingOperation.</returns>
        internal static PendingOperation FromParameterComponent(ParameterComponent component)
        {
            var operationTypeString = component.Part.First(x => x.Name == "type").Value.ToString();
            PatchOperationType operationType;
            try
            {
                operationType = (PatchOperationType)Enum.Parse(
                    typeof(PatchOperationType),
                    operationTypeString.ToUpper(new CultureInfo("en-US", false)));
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException($"Operation type of ${operationTypeString} is not valid.");
            }

            var path = component.Part.First(x => x.Name == "path").Value.ToString();
            var name = component.Part.FirstOrDefault(x => x.Name == "name")?.Value.ToString();
            var value = component.Part.FirstOrDefault(x => x.Name == "value");

            int? index = int.TryParse(component.Part.FirstOrDefault(x => x.Name == "index")?.Value.ToString(), out int itmp) ? itmp : null;
            int? source = int.TryParse(component.Part.FirstOrDefault(x => x.Name == "source")?.Value.ToString(), out int stmp) ? stmp : null;
            int? destination = int.TryParse(component.Part.FirstOrDefault(x => x.Name == "destination")?.Value.ToString(), out int dtmp) ? dtmp : null;

            return new PendingOperation
            {
                Type = operationType,
                Parameter = component,
                Path = path,
                Name = name,
                Value = value,
                Index = index,
                Source = source,
                Destination = destination,
            }.Validate();
        }

        // Validate the required parameters are provided via http://www.hl7.org/fhir/fhirpatch.html
        private PendingOperation Validate()
        {
            if (string.IsNullOrEmpty(Path))
            {
                throw new InvalidOperationException($"Path is required for operation type of {Type}");
            }

            if (string.IsNullOrEmpty(Name) && Type == PatchOperationType.ADD)
            {
                throw new InvalidOperationException($"Name is required for operation type of {Type}");
            }

            if (Value is null &&
                new[] { PatchOperationType.ADD, PatchOperationType.INSERT, PatchOperationType.REPLACE }.Contains(Type))
            {
                throw new InvalidOperationException($"Value is required for operation type of {Type}");
            }

            if (Index is null && Type == PatchOperationType.INSERT)
            {
                throw new InvalidOperationException($"Index is required for operation type of {Type}");
            }

            if (Source is null && Type == PatchOperationType.MOVE)
            {
                throw new InvalidOperationException($"Source is required for operation type of {Type}");
            }

            if (Destination is null && Type == PatchOperationType.MOVE)
            {
                throw new InvalidOperationException($"Destination is required for operation type of {Type}");
            }

            return this;
        }
    }
}
