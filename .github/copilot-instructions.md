# Copilot Instructions for Microsoft FHIR Server

## Code Style
- Follow Microsoft coding conventions for C#
- Use camel case for private fields with an underscore prefix (_fieldName)
- Use Pascal case for properties, methods, and public members
- Include XML documentation for all public members
- On creation of new class, ensure there is a new line between namespace and class declaration

## SQL migration
- For adding new SQL schema version, follow these steps
  - Increment the version number in the SchemaVersion.cs file
  - Update the Max version in the SchemaVersionConstants.cs file
  - Create a new migration file with a format `Version.diff.sql` in ./src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Migrations folder
  - Update the Microsoft.Health.Fhir.SqlServer.csproj file to include the new version in LatestSchemaVersion property
- Let the developer know that they need to add new sql data store for the corresponding functionality and specify the new version.
- Let the developer know that they need to add new integration tests for the corresponding sql changes.
- Let the developer know to follow SQL guidelines from ./docs/SchemaVersioning.md

## Testing Requirements
- Write unit tests for all new functionality
- Use xUnit for testing framework
- Follow Arrange-Act-Assert pattern in tests
- Use Substitute for external dependencies in tests