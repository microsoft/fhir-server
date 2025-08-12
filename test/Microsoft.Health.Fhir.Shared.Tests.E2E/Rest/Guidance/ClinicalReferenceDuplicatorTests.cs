// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using DotLiquid.Util;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Guidance;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Extensions;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Import;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Guidance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ClinicalReferenceDuplicatorTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly HttpIntegrationTestFixture _fixture;

        public ClinicalReferenceDuplicatorTests(HttpIntegrationTestFixture fixture)
        {
            _fixture = fixture;
        }

        private TestFhirClient Client => _fixture.TestFhirClient;

        [Theory]
        [MemberData(nameof(GetCreateResourceData))]
        public async Task GivenResource_WhenCreating_ThenDuplicateResourceShouldBeCreated(
            Resource resource)
        {
            var supportsClinicalReferenceDuplicate = _fixture.TestFhirServer.Metadata.SupportsOperation(
                OperationsConstants.ClinicalReferenceDuplicate);
            await CreateResourceAsync(resource);
        }

        private async Task CreateResourceAsync(Resource resource)
        {
            var resourceType = resource.TypeName;
            var duplicateResourceType = string.Equals(resourceType, KnownResourceTypes.DiagnosticReport)
                ? KnownResourceTypes.DocumentReference : KnownResourceTypes.DiagnosticReport;

            // Create a resource.
            TagResource(resource);
            var response = await Client.CreateAsync(
                resourceType,
                resource);
            Assert.Equal(HttpStatusCode.Created, response.Response.StatusCode);
            Assert.NotNull(response.Resource);

            // Look for a duplicate resource.
            var searchResponse = await Client.SearchAsync(
                $"{duplicateResourceType}?_tag={ClinicalReferenceDuplicator.TagDuplicateOf}|{response.Resource?.Id}");
            Assert.NotNull(searchResponse.Resource?.Entry);
            Assert.Single(searchResponse.Resource.Entry);

            // Check if a duplicate resource has the subject and attachments that match ones from a resource created.
            if (string.Equals(duplicateResourceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase))
            {
                var original = (DocumentReference)resource;
                var duplicate = (DiagnosticReport)searchResponse.Resource.Entry[0].Resource;

                Assert.Equal(original.Subject?.Reference, duplicate.Subject?.Reference);
                Assert.NotNull(duplicate.PresentedForm);

                if (original.Content?.Any(x => !string.IsNullOrEmpty(x.Attachment?.Url)) ?? false)
                {
                    foreach (var a in original.Content.Select(x => x.Attachment))
                    {
                        Assert.Contains(
                            duplicate.PresentedForm,
                            x => string.Equals(x.Url, a.Url, StringComparison.OrdinalIgnoreCase));
                    }
                }
                else
                {
                    Assert.Equal(0, duplicate.PresentedForm.Count(x => !string.IsNullOrEmpty(x.Url)));
                }
            }
            else
            {
                var original = (DiagnosticReport)resource;
                var duplicate = (DocumentReference)searchResponse.Resource.Entry[0].Resource;

                Assert.Equal(original.Subject?.Reference, duplicate.Subject?.Reference);
                Assert.NotNull(duplicate.Content);

                if (original.PresentedForm?.Any(x => !string.IsNullOrEmpty(x.Url)) ?? false)
                {
                    foreach (var a in original.PresentedForm)
                    {
                        Assert.Contains(
                            duplicate.Content,
                            x => string.Equals(x.Attachment?.Url, a.Url, StringComparison.OrdinalIgnoreCase));
                    }
                }
                else
                {
                    Assert.Equal(0, duplicate.Content.Count(x => !string.IsNullOrEmpty(x.Attachment?.Url)));
                }
            }
        }

        private static void TagResource(Resource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            if (resource.Meta == null)
            {
                resource.Meta = new Meta();
            }

            if (resource.Meta.Tag != null)
            {
                resource.Meta.Tag = new List<Coding>();
            }

            var tag = $"clinicalreftest-{DateTime.UtcNow.Ticks}";
            resource.Meta.Tag.Add(new Coding("testTag", tag));
        }

        public static IEnumerable<object[]> GetCreateResourceData()
        {
            var data = new[]
            {
                new object[]
                {
                    // Create a new DiagnosticReport resource with one attachment.
                    new DiagnosticReport
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "12345",
                                },
                            },
                        },
                        Subject = new ResourceReference(Guid.NewGuid().ToString()),
                        PresentedForm = new List<Attachment>
                        {
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/Binary/attachment",
                            },
                        },
                    },
                },
                new object[]
                {
                    // Create a new DiagnosticReport resource with multiple attachments.
                    new DiagnosticReport
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "12345",
                                },
                            },
                        },
                        Subject = new ResourceReference(Guid.NewGuid().ToString()),
                        PresentedForm = new List<Attachment>
                        {
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/Binary/attachment",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2006-12-24",
                                Url = "http://example.org/fhir/Binary/attachment1",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2007-12-24",
                                Url = "http://example.org/fhir/Binary/attachment2",
                            },
                            new Attachment()
                            {
                                ContentType = "application/xhtml",
                                Creation = "2005-12-24",
                                Url = "http://example.org/fhir/Binary/attachment3",
                            },
                        },
                    },
                },
                new object[]
                {
                    // Create a new DiagnosticReport resource without any attachment.
                    new DiagnosticReport
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = DiagnosticReport.DiagnosticReportStatus.Registered,
                        Code = new CodeableConcept()
                        {
                            Coding = new List<Coding>()
                            {
                                new Coding()
                                {
                                    Code = "12345",
                                },
                            },
                        },
                        Subject = new ResourceReference(Guid.NewGuid().ToString()),
                    },
                },
                new object[]
                {
                    // Create a new DocumentReference resource with one attachment.
                    new DocumentReference
                    {
                        Id = Guid.NewGuid().ToString(),
                        Content = new List<DocumentReference.ContentComponent>
                        {
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment",
                                },
                            },
                        },
                        Subject = new ResourceReference(Guid.NewGuid().ToString()),
    #if R4 || R4B || Stu3
                        Status = DocumentReferenceStatus.Current,
    #else
                        Status = DocumentReference.DocumentReferenceStatus.Current,
    #endif
                    },
                },
                new object[]
                {
                    // Create a new DocumentReference resource with multiple attachments.
                    new DocumentReference
                    {
                        Id = Guid.NewGuid().ToString(),
                        Content = new List<DocumentReference.ContentComponent>
                        {
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2006-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment1",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2007-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment2",
                                },
                            },
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    ContentType = "application/xhtml",
                                    Creation = "2005-12-24",
                                    Url = "http://example.org/fhir/Binary/attachment3",
                                },
                            },
                        },
                        Subject = new ResourceReference(Guid.NewGuid().ToString()),
    #if R4 || R4B || Stu3
                        Status = DocumentReferenceStatus.Current,
    #else
                        Status = DocumentReference.DocumentReferenceStatus.Current,
    #endif
                    },
                },
                new object[]
                {
                    // Create a new DocumentReference resource without any attachment.
                    new DocumentReference
                    {
                        Id = Guid.NewGuid().ToString(),
                        Subject = new ResourceReference(Guid.NewGuid().ToString()),
    #if R4 || R4B || Stu3
                        Status = DocumentReferenceStatus.Current,
    #else
                        Status = DocumentReference.DocumentReferenceStatus.Current,
    #endif
                    },
                },
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }
    }
}
