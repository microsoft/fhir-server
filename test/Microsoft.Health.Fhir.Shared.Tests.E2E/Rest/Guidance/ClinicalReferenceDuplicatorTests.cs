// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
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
            await CreateResourceAsync(resource);
        }

        [Theory]
        [MemberData(nameof(GetCreateResourceData))]
        public async Task GivenResource_WhenDeleting_ThenDuplicateResourceShouldBeDeleted(
            Resource resource)
        {
            (var source, var duplicate) = await CreateResourceAsync(resource);
            await DeleteResourceAsync(source);
        }

        [Theory]
        [MemberData(nameof(GetUpdateResourceData))]
        public async Task GivenResource_WhenUpdating_ThenDuplicateResourceShouldBeUpdated(
            Resource resource,
            string subject,
            List<Attachment> attachments)
        {
            (var source, var duplicate) = await CreateResourceAsync(resource);
            await UpdateResourceAsync(source, subject, attachments);
        }

        private async Task<(Resource source, Resource duplicate)> CreateResourceAsync(Resource resource)
        {
            var resourceType = resource.TypeName;
            var duplicateResourceType = string.Equals(resourceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase)
                ? KnownResourceTypes.DocumentReference : KnownResourceTypes.DiagnosticReport;

            // Create a resource.
            TagResource(resource);
            var response = await Client.CreateAsync(
                resource.TypeName,
                resource);
            Assert.Equal(HttpStatusCode.Created, response.Response.StatusCode);
            Assert.NotNull(response.Resource);

            // Look for a duplicate resource.
            var searchResponse = await Client.SearchAsync(
                $"{duplicateResourceType}?_tag={ClinicalReferenceDuplicator.TagDuplicateOf}|{response.Resource?.Id}");
            Assert.NotNull(searchResponse.Resource?.Entry);

            // Check if a duplicate resource has the subject and attachments that match ones from a resource created.
            var source = response.Resource;
            var duplicate = searchResponse.Resource?.Entry?.FirstOrDefault()?.Resource;
            if (duplicate != null)
            {
                ValidateResourceProperties(source, duplicate);
            }

            return (source, duplicate);
        }

        private async Task DeleteResourceAsync(Resource resource)
        {
            // Delete a resource.
            var response = await Client.DeleteAsync(resource);
            Assert.Equal(HttpStatusCode.NoContent, response.Response.StatusCode);

            // Look for a duplicate resource.
            var searchResponse = await Client.SearchAsync(
                $"{resource.TypeName}?_tag={ClinicalReferenceDuplicator.TagDuplicateOf}|{resource.Id}");
            Assert.Equal(0, searchResponse.Resource?.Entry?.Count ?? 0);
        }

        private async Task<(Resource source, Resource duplicate)> UpdateResourceAsync(
            Resource resource,
            string subject,
            List<Attachment> attachments)
        {
            var resourceType = resource.TypeName;
            var duplicateResourceType = KnownResourceTypes.DocumentReference;

            if (string.Equals(resourceType, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase))
            {
                var diagnosticReport = (DiagnosticReport)resource;
                diagnosticReport.Subject = new ResourceReference(subject);
                diagnosticReport.PresentedForm = attachments;
            }
            else
            {
                var documentReference = (DocumentReference)resource;
                documentReference.Subject = new ResourceReference(subject);

                var contents = new List<DocumentReference.ContentComponent>();
                foreach (var attachment in attachments)
                {
                    contents.Add(
                        new DocumentReference.ContentComponent()
                        {
                            Attachment = attachment,
                        });
                }

                documentReference.Content = contents;
                duplicateResourceType = KnownResourceTypes.DiagnosticReport;
            }

            // Update a resource.
            var response = await Client.UpdateAsync(resource);
            Assert.Equal(HttpStatusCode.OK, response.Response.StatusCode);
            Assert.NotNull(response.Resource);

            // Look for a duplicate resource.
            var searchResponse = await Client.SearchAsync(
                $"{duplicateResourceType}?_tag={ClinicalReferenceDuplicator.TagDuplicateOf}|{response.Resource?.Id}");
            Assert.NotNull(searchResponse.Resource?.Entry);

            // Check if a duplicate resource has the subject and attachments that match ones from a resource created.
            var source = response.Resource;
            var duplicate = searchResponse.Resource?.Entry?.FirstOrDefault()?.Resource;
            if (duplicate != null)
            {
                ValidateResourceProperties(source, duplicate);
            }

            return (source, duplicate);
        }

        private static void ValidateResourceProperties(Resource source, Resource duplicate)
        {
            EnsureArg.IsNotNull(source, nameof(source));
            EnsureArg.IsNotNull(duplicate, nameof(duplicate));

            if (string.Equals(source.TypeName, KnownResourceTypes.DiagnosticReport, StringComparison.OrdinalIgnoreCase))
            {
                var diagnosticReport = (DiagnosticReport)source;
                var documentReference = (DocumentReference)duplicate;
                Assert.Equal(diagnosticReport.Subject?.Reference, documentReference.Subject?.Reference);
                Assert.Equal(
                    diagnosticReport.PresentedForm?.Count(x => !string.IsNullOrEmpty(x.Url)),
                    documentReference.Content?.Count(x => !string.IsNullOrEmpty(x.Attachment?.Url)));

                if (diagnosticReport.PresentedForm?.Any(x => !string.IsNullOrEmpty(x.Url)) ?? false)
                {
                    foreach (var a in diagnosticReport.PresentedForm.Where(x => !string.IsNullOrEmpty(x.Url)))
                    {
                        Assert.Contains(
                            documentReference.Content,
                            x => string.Equals(x.Attachment?.Url, a.Url, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
            else
            {
                var documentReference = (DocumentReference)source;
                var diagnosticReport = (DiagnosticReport)duplicate;
                Assert.Equal(documentReference.Subject?.Reference, diagnosticReport.Subject?.Reference);
                Assert.Equal(
                    documentReference.Content?.Count(x => !string.IsNullOrEmpty(x.Attachment?.Url)),
                    diagnosticReport.PresentedForm?.Count(x => !string.IsNullOrEmpty(x.Url)));

                if (documentReference.Content?.Any(x => !string.IsNullOrEmpty(x.Attachment?.Url)) ?? false)
                {
                    foreach (var a in documentReference.Content.Where(x => !string.IsNullOrEmpty(x?.Attachment?.Url)).Select(x => x.Attachment))
                    {
                        Assert.Contains(
                            diagnosticReport.PresentedForm,
                            x => string.Equals(x?.Url, a.Url, StringComparison.OrdinalIgnoreCase));
                    }
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
                        Content = new List<DocumentReference.ContentComponent>
                        {
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()))),
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
            };

            foreach (var d in data)
            {
                yield return d;
            }
        }

        public static IEnumerable<object[]> GetUpdateResourceData()
        {
            var data = new[]
            {
                new object[]
                {
                    // Create a new DiagnosticReport resource with one attachment. Update with multiple attachments.
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
                    Guid.NewGuid().ToString(),
                    new List<Attachment>
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
                    },
                },
                new object[]
                {
                    // Create a new DiagnosticReport resource with multiple attachments. Update with one attachment.
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
                                Creation = "2008-12-24",
                                Url = "http://example.org/fhir/Binary/attachment3",
                            },
                        },
                    },
                    Guid.NewGuid().ToString(),
                    new List<Attachment>
                    {
                        new Attachment()
                        {
                            ContentType = "application/xhtml",
                            Creation = "2009-12-24",
                            Url = "http://example.org/fhir/Binary/attachment4",
                        },
                    },
                },
                new object[]
                {
                    // Create a new DiagnosticReport resource with multiple attachments. Update without any attachment.
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
                    Guid.NewGuid().ToString(),
                    new List<Attachment>(),
                },
                new object[]
                {
                    // Create a new DocumentReference resource with one attachment. Update with multiple attachments.
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
                    Guid.NewGuid().ToString(),
                    new List<Attachment>
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
                    },
                },
                new object[]
                {
                    // Create a new DocumentReference resource with multiple attachments. Update with one attachment.
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
                                    Creation = "2008-12-24",
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
                    Guid.NewGuid().ToString(),
                    new List<Attachment>
                    {
                        new Attachment()
                        {
                            ContentType = "application/xhtml",
                            Creation = "2009-12-24",
                            Url = "http://example.org/fhir/Binary/attachment4",
                        },
                    },
                },
                new object[]
                {
                    // Create a new DocumentReference resource with multiple attachments. Update without any attachment.
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
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()))),
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
                    Guid.NewGuid().ToString(),
                    new List<Attachment>()
                    {
                        new Attachment()
                        {
                            Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()))),
                        },
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
