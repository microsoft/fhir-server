// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents an expression for search limited to a Smart user's compartment.
    /// </summary>
    public class SmartCompartmentSearchExpression : CompartmentSearchExpression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SmartCompartmentSearchExpression"/> class.
        /// </summary>
        /// <param name="compartmentType">The Resource type of the smart user.</param>
        /// <param name="compartmentId">The Resource id of the smart user.</param>
        /// /// <param name="filteredResourceTypes">Resource types to filter</param>
        public SmartCompartmentSearchExpression(string compartmentType, string compartmentId, params string[] filteredResourceTypes)
            : base(compartmentType, compartmentId, filteredResourceTypes)
        {
        }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));
            return visitor.VisitSmartCompartment(this, context);
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(SmartCompartmentSearchExpression));
            hashCode.Add(CompartmentType);
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
            return other is SmartCompartmentSearchExpression compartmentSearch && compartmentSearch.CompartmentType == CompartmentType;
        }
    }
}
