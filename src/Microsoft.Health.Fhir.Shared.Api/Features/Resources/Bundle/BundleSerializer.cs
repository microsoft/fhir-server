// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Utility;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Shared.Core.Features.Search;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public static class BundleSerializer
    {
        /// <summary>
        /// Serializer for Bundles made up of RawBundleEntryComponents
        /// </summary>
        /// <param name="bundle">Bundle</param>
        /// <param name="outputStream">OutputStream</param>
        /// <param name="pretty">Pretty formatting</param>
        public static async Task Serialize(Hl7.Fhir.Model.Bundle bundle, Stream outputStream, bool pretty = false)
        {
            EnsureArg.IsNotNull(bundle, nameof(bundle));
            EnsureArg.IsNotNull(outputStream, nameof(outputStream));

            await using FhirJsonWriter writer = FhirJsonWriter.Create(outputStream, pretty)
                .WriteStartObject()
                .WriteString("resourceType", bundle.TypeName)
                .WriteOptionalString("id", bundle.Id)
                .Condition(
                    bundle.Meta != null,
                    b => b.WriteObject(
                            "meta",
                            _ => b.WriteOptionalString("lastUpdated", bundle.Meta?.LastUpdated?.ToInstantString())))
                .WriteOptionalString("type", bundle.Type?.GetLiteral())
                .Condition(
                    bundle.Link?.Any() == true,
                    b => b.WriteArray(
                        "link",
                        bundle.Link,
                        (_, link) =>
                            b.WriteString("relation", link.Relation)
                                .WriteString("url", link.Url)))
                .WriteOptionalNumber("total", bundle.Total)
                .Condition(
                    bundle.Entry?.Count > 0,
                    b => b.WriteArray(
                        name: "entry",
                        values: bundle.Entry,
                        itemWriter: (_, component) =>
                        {
                            var rawComponent = component as RawBundleEntryComponent;
                            var rawOutcomeComponent = component?.Response as RawBundleResponseComponent;

                            b.WriteOptionalString("fullUrl", component.FullUrl)
                                .ConditionIf(/* Support raw strings */
                                    rawComponent?.ResourceElement != null,
                                    _ => b.WriteRawProperty("resource", rawComponent?.ResourceElement.SerializeToBytes()))
                                .ElseIf(/* Support POCOs */
                                    rawComponent == null && component.Resource != null,
                                    _ => b.WriteRawProperty("resource", component.Resource.ToJsonBytes()))
                                .Condition(
                                    component.Search?.Mode != null,
                                    _ => b.WriteObject(
                                        "search",
                                        _ => b.WriteString("mode", component.Search?.Mode?.GetLiteral())))
                                .Condition(
                                    component.Request != null,
                                    _ => b.WriteObject(
                                        "request",
                                        _ => b.WriteString("method", component.Request?.Method?.GetLiteral())
                                            .WriteString("url", component.Request?.Url)))
                                .Condition(
                                    component.Response != null,
                                    _ => b.WriteObject(
                                        "response",
                                        _ => b.WriteString("status", component.Response.Status)
                                            .WriteOptionalString("etag", component.Response.Etag)
                                            .WriteOptionalString("lastModified", component.Response.LastModified?.ToInstantString())
                                            .ConditionIf(
                                                rawOutcomeComponent?.OutcomeElement != null,
                                                _ => b.WriteRawProperty("outcome", rawOutcomeComponent?.OutcomeElement.SerializeToBytes()))
                                            .ElseIf(
                                                rawOutcomeComponent?.OutcomeElement == null && component.Response.Outcome != null,
                                                _ => b.WriteRawProperty("outcome", component.Response.Outcome.ToJsonBytes()))));
                        }))
                .WriteEndObject();
        }
    }
}
