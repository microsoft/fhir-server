// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using FhirPathPatch.Helpers;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification;
using Microsoft.Health.Fhir.Core.Models;

namespace FhirPathPatch.Operations
{
    /// <summary>
    /// Abstract representation of a basic operational resource.
    /// </summary>
    public abstract class OperationBase
    {
        private ElementNode _target;

        /// <summary>
        /// Initializes a new instance of the <see cref="OperationBase"/> class.
        /// </summary>
        /// <param name="resource">FHIR Resource for this operation.</param>
        /// <param name="po">Operation request.</param>
        protected OperationBase(Resource resource, PendingOperation po)
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

        // Gets the target in the ElementNode tree to execute patch operations.
        internal ElementNode Target =>
            _target is null ? _target = ResourceElement.Find(Operation.Path) : _target;

        // Gets the value of the patch operation as an ElementNode.
        internal ElementNode ValueElementNode
        {
            get
            {
                PropertyMapping summary = Operation.Type is EOperationType.ADD ?
                    Target.Definition.GetChildDefinition(Operation.Name) :
                    Target.Definition as PropertyMapping;

                if (summary != null)
                {
                    return Operation.Value.GetElementNodeFromPart(summary.Name, summary.PropertyTypeMapping.NativeType);
                }

                throw new InvalidOperationException("Patch target has no definition");
            }
        }

        /// <summary>
        /// All inheriting classes must implement an operation exeuction method.
        /// </summary>
        /// <returns>FHIR Resource as POCO.</returns>
        public abstract Resource Execute();
    }
}
