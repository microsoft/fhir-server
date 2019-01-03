// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents an expression for search performed for a compartment.
    /// </summary>
    public class CompartmentSearchExpression : Expression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CompartmentSearchExpression"/> class.
        /// </summary>
        /// <param name="compartmentType">The compartment type.</param>
        /// <param name="compartmentId">The compartment id.</param>
        public CompartmentSearchExpression(CompartmentType compartmentType, string compartmentId)
        {
            EnsureArg.IsTrue(Enum.IsDefined(typeof(CompartmentType), compartmentType), nameof(compartmentType));
            EnsureArg.IsNotNullOrWhiteSpace(compartmentId, nameof(compartmentId));

            CompartmentType = compartmentType;
            CompartmentId = compartmentId;
        }

        /// <summary>
        /// The compartment type.
        /// </summary>
        public CompartmentType CompartmentType { get; }

        /// <summary>
        /// The compartment id.
        /// </summary>
        public string CompartmentId { get; }

        protected internal override void AcceptVisitor(IExpressionVisitor visitor)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));
            visitor.Visit(this);
        }
    }
}
