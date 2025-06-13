// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Helpers;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Operations
{
    /// <summary>
    /// This handles patching an object that requires updating the existing element or adding a new element if it doesn't exists.
    /// </summary>
    internal class OperationUpsert : OperationBase
    {
        internal OperationUpsert(Resource resource, PendingOperation po)
            : base(resource, po)
        {
        }

        /// <summary>
        /// Gets the value of the patch operation as an ElementNode.
        /// </summary>
        internal override ElementNode ValueElementNode =>
            Operation.Value.GetElementNodeFromPart(Target.Definition.GetChildMapping(Operation.Name));

        /// <summary>
        /// Executes a FHIRPath Patch Add operation if the element doesn't exists. Upsert operations will add a new
        /// element of the specified opearation.Name at the specified operation.Path.
        ///
        /// Adding of a non-existent element will create the new element. Add on
        /// a list will append the list.
        ///
        /// Executes a FHIRPath Patch Replace operation if the element already exists. Upsert operations will
        /// replace the element located at operation.Path with a new value
        /// specified at operation.Value.
        ///
        /// Fhir package has a built-in "Add" and "Upsert" operation which accomplishes this
        /// easily.
        /// </summary>
        /// <returns>Patched FHIR Resource as POCO.</returns>
        internal override Resource Execute()
        {
            try
            {
                Target = ResourceElement
                            .Select(Operation.Path)
                            .RequireOneOrMoreElements()
                            .GetFirstElementNode();

                // Select the element we want to Upsert
                var targetAtName = Target.Select(Operation.Name).ToList();

                if (targetAtName.Count == 0) // If the element doesn't exist
                {
                    // If we couldn't find an element or a collection then, we can ADD it
                    try
                    {
                        Target = ResourceElement
                                    .Select(Operation.Path)
                                    .RequireOneOrMoreElements()
                                    .RequireMultipleElementsInSameCollection()
                                    .GetFirstElementNode();

                        Target.Add(Provider, ValueElementNode);
                    }
                    catch (InvalidOperationException ex)
                    {
                        throw new InvalidOperationException($"{ex.Message} at {Operation.Path} when processing patch upsert operation.", ex);
                    }
                }
                else // If the element exists
                {
                    if (targetAtName.Any(x => !x.Definition.IsCollection))
                    {
                        // If it is not a collection, we need to just replace the element with a new value
                        try
                        {
                            Target = ResourceElement
                                        .Select(Operation.Path)
                                        .RequireOneOrMoreElements()
                                        .RequireSingleElement()
                                        .GetFirstElementNode();

                            Target.ReplaceWith(Provider, ValueElementNode);
                        }
                        catch (InvalidOperationException ex)
                        {
                            throw new InvalidOperationException($"{ex.Message} at {Operation.Path} when processing patch upsert operation.", ex);
                        }
                    }
                    else if (targetAtName.Any(x => x.Definition.IsCollection))
                    {
                        // If it is a collection
                        // If something already exists then do not do anything
                        try
                        {
                            var exists = targetAtName.Any(node => node.IsEqualTo(ValueElementNode).Success);
                            if (!exists) // If something new then append it to the collection
                            {
                                Target = ResourceElement
                                    .Select(Operation.Path)
                                    .RequireOneOrMoreElements()
                                    .RequireMultipleElementsInSameCollection()
                                    .GetFirstElementNode();

                                Target.Add(Provider, ValueElementNode);
                            }

                            // TODO: Or should we just replace?
                            //////Target = ResourceElement
                            //////            .Select(Operation.Path)
                            //////            .RequireOneOrMoreElements()
                            //////            .RequireSingleElement()
                            //////            .GetFirstElementNode();

                            //////Target.ReplaceWith(Provider, ValueElementNode);
                        }
                        catch (InvalidOperationException ex)
                        {
                            throw new InvalidOperationException($"{ex.Message} at {Operation.Path} when processing patch upsert operation.", ex);
                        }
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"{ex.Message} at {Operation.Path} when processing patch upsert operation.", ex);
            }

            return ResourceElement.ToPoco<Resource>();
        }
    }
}
