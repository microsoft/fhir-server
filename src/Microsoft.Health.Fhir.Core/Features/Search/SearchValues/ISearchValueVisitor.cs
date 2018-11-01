// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    public interface ISearchValueVisitor
    {
        /// <summary>
        /// Visits the <see cref="CompositeSearchValue"/>.
        /// </summary>
        /// <param name="composite">The composite search value to visit.</param>
        void Visit(CompositeSearchValue composite);

        /// <summary>
        /// Visits the <see cref="DateTimeSearchValue"/>.
        /// </summary>
        /// <param name="dateTime">The date time search value to visit.</param>
        void Visit(DateTimeSearchValue dateTime);

        /// <summary>
        /// Visits the <see cref="NumberSearchValue"/>.
        /// </summary>
        /// <param name="number">The number search value to visit.</param>
        void Visit(NumberSearchValue number);

        /// <summary>
        /// Visits the <see cref="QuantitySearchValue"/>.
        /// </summary>
        /// <param name="quantity">The quantity search value to visit.</param>
        void Visit(QuantitySearchValue quantity);

        /// <summary>
        /// Visits the <see cref="ReferenceSearchValue"/>.
        /// </summary>
        /// <param name="reference">The reference search value to visit.</param>
        void Visit(ReferenceSearchValue reference);

        /// <summary>
        /// Visits the <see cref="StringSearchValue"/>.
        /// </summary>
        /// <param name="s">The string search value to visit.</param>
        void Visit(StringSearchValue s);

        /// <summary>
        /// Visits the <see cref="TokenSearchValue"/>.
        /// </summary>
        /// <param name="token">The token search value to visit.</param>
        void Visit(TokenSearchValue token);

        /// <summary>
        /// Visits the <see cref="UriSearchValue"/>.
        /// </summary>
        /// <param name="uri">The URI search value to visit.</param>
        void Visit(UriSearchValue uri);
    }
}
