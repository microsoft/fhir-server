// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public interface ISearchParameterSupportResolver
    {
        /// <summary>
        /// Determines if the given search parameter is able to be indexed, and computes a small
        /// set of derived metadata flags that depend on the same FhirPath compile + type-resolution work.
        /// </summary>
        /// <param name="parameterInfo">Search Parameter info</param>
        /// <returns>
        /// <para><c>Supported</c> — the system can index and search this parameter.</para>
        /// <para><c>IsPartiallySupported</c> — the parameter resolves to multiple types and only some can be indexed.</para>
        /// <para>
        /// <c>IsDateOnly</c> — every type-resolution result for the parameter's expression has FhirNodeType "date".
        /// True only for FHIR-spec <c>date</c>-typed search parameters (e.g. <c>Patient.birthDate</c>) — never for
        /// parameters that resolve to <c>dateTime</c>, <c>instant</c>, <c>Period</c>, or <c>Timing</c>. Used by SQL-side
        /// rewriters to collapse the (StartDateTime, EndDateTime) overlap predicate into a single-column equality.
        /// Conservative: a custom parameter whose <c>.as(date)</c> / <c>.ofType(date)</c> intent is not detectable
        /// from the FhirPath expression yields <c>false</c>, forfeiting the optimization but never producing wrong results.
        /// </para>
        /// <para>
        /// <c>IsScalarTemporal</c> — every type-resolution result for the parameter's expression has scalar temporal
        /// FhirNodeType <c>date</c>, <c>dateTime</c>, or <c>instant</c>, with no <c>Period</c>, <c>Timing</c>, or other
        /// range-capable temporal type. Used for SQL-side diagnostics and allow-listed query optimizations.
        /// </para>
        /// </returns>
        (bool Supported, bool IsPartiallySupported, bool IsDateOnly, bool IsScalarTemporal) IsSearchParameterSupported(SearchParameterInfo parameterInfo);
    }
}
