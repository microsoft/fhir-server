// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.GraphQl.DataLoader;
using Microsoft.Health.Fhir.Shared.Api.Features.GraphQl;
using Snapshooter.Xunit;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.GraphQl
{
    public class PatientTests
    {
        [Fact]
        [System.Obsolete]
        public async System.Threading.Tasks.Task<Task> GetPatientsAsync()
        {
            // arrange
            IRequestExecutor executor = (IRequestExecutor)new ServiceCollection()
                .AddRouting()

                // Adding the GraphQL server core service
                .AddGraphQLServer()

                // Adding our scheme
                .AddDocumentFromFile("./patient.graphql")
                .AddDocumentFromFile("./types.graphql")

                // Next we add the types to our schema
                .AddQueryType(d => d.Name("Query"))
                    .AddTypeExtension<PatientQueries>()
                .BindComplexType<Address>()
                .BindComplexType<Attachment>()
                .BindComplexType<Code>()
                .BindComplexType<CodeableConcept>()
                .BindComplexType<Coding>()
                .BindComplexType<ContactPoint>()
                .BindComplexType<DataType>()
                .BindComplexType<DomainResource>()
                .BindComplexType<Element>()
                .BindComplexType<Extension>()
                .BindComplexType<FhirBoolean>()
                .BindComplexType<FhirDateTime>()
                .BindComplexType<HumanName>()
                .BindComplexType<Identifier>()
                .BindComplexType<Meta>()
                .BindComplexType<Narrative>()
                .BindComplexType<Patient>()
                .BindComplexType<Patient.LinkComponent>()
                .BindComplexType<Patient.CommunicationComponent>()
                .BindComplexType<Patient.ContactComponent>()
                .BindComplexType<Period>()
                .BindComplexType<PrimitiveType>()
                .BindComplexType<Resource>()
                .BindComplexType<ResourceReference>()

                // Adding DataLoader to our system
                .AddDataLoader<PatientByIdDataLoader>();

            // act
            IExecutionResult result = await executor.ExecuteAsync(@"
                patients {
                    id
                }");

            // assert
            result.ToJson().MatchSnapshot();
        }
    }
}
