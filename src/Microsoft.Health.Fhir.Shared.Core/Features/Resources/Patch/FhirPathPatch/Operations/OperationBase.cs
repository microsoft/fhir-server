// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Helpers;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Operations
{
    /// <summary>
    /// Abstract representation of a basic operational resource.
    /// </summary>
    internal abstract class OperationBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OperationBase"/> class.
        /// </summary>
        /// <param name="resource">FHIR Resource for this operation.</param>
        /// <param name="po">Operation request.</param>
        internal OperationBase(Resource resource, PendingOperation po)
        {
            ResourceElement = resource.ToElementNode();
            Operation = po;
        }

        // Gets the element node representation of the patch operation resource
        internal ElementNode ResourceElement { get; }

        // Gets the operation object representing the patch request.
        internal PendingOperation Operation { get; }

        // Gets the FHIR Provider used in manipulating ElementNodes.
        internal static IStructureDefinitionSummaryProvider Provider =>
            ModelInfoProvider.Instance.StructureDefinitionSummaryProvider;

        internal ElementNode Target { get; set; }

        // Gets the value of the patch operation as an ElementNode.
        internal virtual ElementNode ValueElementNode =>
             Operation.Value.GetElementNodeFromPart(Target.Definition as PropertyMapping);

        /// <summary>
        /// All inheriting classes must implement an operation exeuction method.
        /// </summary>
        /// <returns>FHIR Resource as POCO.</returns>
        internal abstract Resource Execute();
    }
}
