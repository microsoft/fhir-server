﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Represents a composite search value.
    /// </summary>
    public class CompositeSearchValue : ISearchValue
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CompositeSearchValue"/> class.
        /// </summary>
        /// <param name="components">The composite component values.</param>
        public CompositeSearchValue(IReadOnlyList<IReadOnlyList<ISearchValue>> components)
        {
            EnsureArg.IsNotNull(components, nameof(components));
            EnsureArg.HasItems(components, nameof(components));

            Components = components;
        }

        /// <summary>
        /// Gets the composite component values.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<ISearchValue>> Components { get; }

        /// <inheritdoc />
        public bool IsValidAsCompositeComponent => false;

        /// <inheritdoc />
        public void AcceptVisitor(ISearchValueVisitor visitor)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            visitor.Visit(this);
        }

        public bool Equals([AllowNull] ISearchValue other)
        {
            if (other == null)
            {
                return false;
            }

            var compositeSearchValueOther = other as CompositeSearchValue;

            if (compositeSearchValueOther == null)
            {
                return false;
            }

            return Components.SequenceEqual(compositeSearchValueOther.Components);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Join(" $ ", Components.Select(component => string.Join(", ", component.Select(v => $"({v})"))));
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(Components.Select(x => x.GetHashCode()));
        }
    }
}
