# Schema Migration Guide
This document describes how to perform Sql schema migration on the Open source FHIR server.

 A database schema will evolve over time. The corresponding server in production would need to be upgraded to the next/latest version of the code, which may require schema migration.

A migration is a T-SQL script that alters the database in some way. The script has a version encoded in its file name. This version is an incrementing integer.

## There are two ways to upgrade the schema
1. Automatic schema upgrade

2. Schema upgrade via schema migration tool

## 1. Automatic Schema Upgrade
Here, the schema migration is performed automatically on the server startup.
The scripts that upgrade schema are part of any upgrade package for the application and run automatically when the application launches.

In case of any errors during upgrade, the application fails to start and the upgrade script transaction is not committed.

- ### Prerequisites

    - Application should have admin access to the database.

- ### When might you choose this option
    -  There is no specific DBA role - In this case, automatic schema upgrade is a good choice as it just requires the application with admin access to the database.

    - Non-Production scenarios - The auto upgrade is a good option for dev/tests environments. This will automatically apply any pending migrations when the application launches. Hence, the testing of schema upgrade becomes part of the test cycle for the application.

## 2. Upgrade via Schema migration tool
Schema migration tool is a command-line utility that admins can run to perform schema migrations on demand.

Schema upgrade scripts would still be part of any upgrade package for the application but the scripts will not run automatically on the server startup. In order to perform the schema upgrade, the schema admin would need to run the schema migration tool.

 - ### Prerequisites

    - The schema migration tool is available.

 - ### When might you choose this option
    - Production environment
 
        It is highly recommended to use schema migration tool for the production environment to avoid downtime. Automatic schema migrations can take a long time, often in proportion to the size of the database. For this reason, performing schema migrations on startup may not be a good option.
 
    - Role-based security
 
        Only Database/Schema Admin can run this tool to upgrade the schema. It allows you to setup proper roles and permissions for the application e.g. the application is assigned the role as only Database Reader/Writer.

- ### Schema migration tool

    The detailed information about this tool is provided [here](SchemaMigrationTool.md).

 - ### Best practices for schema upgrade on Sql server
    - #### Back up the data before executing.
        
        In case something goes wrong during the implementation, you can’t afford to lose data. Make sure there are backup resources and that they’ve been tested before you proceed.
        
    - #### Stick to the strategy.

        Too many data managers make a plan and then abandon it when the process goes “too” smoothly or when things get out of hand. The migration process can be complicated, so prepare for that reality and then 
        stick to the plan.

    - #### Test, test

        Test before executing the migration and test after the migration is completed.

 ## How to toggle between the options
There is a configurable property in the launchSettings.json which can be set to true/false based on the desired behavior.

`SqlServer:SchemaOptions:AutomaticUpdatesEnabled": "true"`

* If the property sets to 'true', then the schema would be upgraded automatically on the server startup.
* If the property sets to 'false', then the schema would remain in the same state. Moreover, the schema would only be upgraded by running the schema migration tool as needed.

This configurable property should be considered for each web project

* Stu3 - https://github.com/microsoft/fhir-server/blob/master/src/Microsoft.Health.Fhir.Stu3.Web/Properties/launchSettings.json#L27
* R4 - https://github.com/microsoft/fhir-server/blob/master/src/Microsoft.Health.Fhir.R4.Web/Properties/launchSettings.json#L28
* R5 - https://github.com/microsoft/fhir-server/blob/master/src/Microsoft.Health.Fhir.R5.Web/Properties/launchSettings.json#L28

 Note: This guide contains generalized advice and may not take your specific environment or application into account.

 ## Frequently Asked Questions

 Q1. What is upgraded first, app service or database schema?

 Ans. The app service is upgraded first. Because firstly it contains the scripts to migrate the schema and secondly the code is considered to work for a number of schema versions and will specify the min and max supported versions.

 Q2. What should we do in case of automatic schema upgrade fails?

 Ans. If automatic schema upgrade fails and application is unable to start then you can toggle the schema auto upgrade flag as mentioned under the section (How to toggle between the options) so that the upgrade scripts would not run and application would start on the current version.
 Also, you can reach out to the concerned team with the error description to get further help on the issue.
