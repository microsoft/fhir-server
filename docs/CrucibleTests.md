# Crucible tests

Crucible is an open source web application for testing FHIR servers. The project is hosted on Github at https://github.com/fhir-crucible/crucible, and live at https://www.projectcrucible.org/.

This project contains integration tests that can utilize an instance of Crucible to run tests against a deployed instance of the FHIR server and report these outcomes as standard XUnit tests.
The integration tests are located at the path: _/test/Microsoft.Health.Fhir.IntegrationTests/Crucible_.

## CI

These integration tests run as part of the CI build, they rely on two environment variables to be passed in from the build server, these are:

| Varible | Description |
| ------- | ----------- |
| CrucibleEnvironmentUrl | The url to the Crucible server
| TestEnvironmentUrl | The url to the deployed FHIR instance

The tests will wait for the Crucible server to execute the suite, then will display the results a an XUnit Theory data driven test. This allows the tests to have a result item for each suite. If there was a failure, a description and permalink to the Crucible server will be displayed.
