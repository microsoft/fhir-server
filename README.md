# FHIR Server for Azure

A .NET Core implementation of the FHIR standard.

[![CI Status](https://microsofthealthoss.vsrm.visualstudio.com/_apis/public/Release/badge/7621b231-1a7d-4364-935b-2f72b911c43d/1/1)](https://microsofthealthoss.visualstudio.com/FhirServer/_releases2)

FHIR Server for Azure is an open source implementation of the emerging HL7 Fast Healthcare Interoperability Resources (FHIR) specification designed for the Microsoft cloud. The FHIR specification defines how clinical health data can be made interoperable across systems, and the FHIR Server for Azure helps enables that interoperability in the cloud. 

The FHIR specification helps enable compliant healthcare data transmission, storage and sharing. This “data liquidity” enables healthcare providers, clinical researchers, payors, biotech labs, and other health and life science organizations to exchange and integrate clinical data in the pursuit of improved outcomes and efficiencies. 

Bringing health data into Azure enables new cloud and AI scenarios and use cases in research, clinical trials, population health management, and other healthcare innovations. Health data liquidity is necessary to unlocking interoperability with the primary system of record — the Electronic Health Record (EHR) — and contributing clinical data that is enriched through analytics and machine learning in the cloud back into the EHR. 

FHIR Server for Azure provides an opportunity to drive interoperability with traditional health data as well as evolving and emerging data types (e.g. big data, IoMT, genomics and immunomics). 

Privacy and security are top priorities and the FHIR Server for Azure has been developed in support of requirements for Protected Health Information (PHI). All the Azure services used in FHIR Server for Azure [meet the compliance requirements for Protected Health Information](https://www.microsoft.com/en-us/trustcenter/compliance/complianceofferings).

This open source project is fully backed by a Microsoft engineering team in the Microsoft Healthcare group, but we know that this project will only get better with your feedback and contributions. We are actively driving the development of this code base and tests builds and deployments daily. 

# Documentation

- [Deployment](docs/DefaultDeployment.md): Describes how to deploy FHIR Server for Azure. 
- [Azure Active Directory Application Registrations](docs/PortalAppRegistration.md): Describes how to configure Azure Active Directory (AAD) for use with FHIR Server for Azure.
- [Authentication](docs/Authentication.md): Describes the authentication settings for the FHIR server and how to make use of it in development and test scenarios.
- [Roles](docs/Roles.md): Describes how the FHIR Server for Azure role-based access control (RBAC) system works.
- [Search](docs/Search.md): Describes how search is implemented for the FHIR Server for Azure.
- [Debugging](docs/HowToDebug.md): Describes how to debug FHIR Server for Azure using Visual Studio.
- [Testing with Postman](docs/PostmanTesting.md): Describes how to use Postman for testing FHIR Server for Azure.



## More Information 

- [FHIR Server for Azure Demo by Michael Hansen, Senior Program Manager](https://github.com/hansenms/FhirDemo)
- Blog: [FHIR Server for Azure, an open source project for modern healthcare](https://cloudblogs.microsoft.com/industry-blog/industry/health/fhir-server-for-azure-an-open-source-project-for-modern-healthcare/)
- Twitter: [Health_IT](https://twitter.com/Health_IT)

## Contributing
This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.microsoft.com.

When you submit a pull request, a CLA-bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

There are many other ways to contribute to FHIR Server for Azure.
- [Submit bugs](issues) and help us verify fixes as they are checked in.
- Review the [source code changes](pulls).
- Engage with FHIR Server for Azure users and developers on [StackOverflow](https://stackoverflow.com/questions/tagged/fhir-server-for-azure).
- Join the [#fhirforazure](https://twitter.com/hashtag/fhirserverforazure?f=tweets&vertical=default) discussion on Twitter.
- [Contribute bug fixes](CONTRIBUTING.md).

See [Contributing to FHIR Server for Azure](CONTRIBUTING.md) for more information.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.


FHIR&reg; is the registered trademark of HL7 and is used with the permission of HL7.

[FHIR Specification](https://www.hl7.org/fhir/)