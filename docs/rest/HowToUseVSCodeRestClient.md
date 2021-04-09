# How to use Visual Studio Code's REST Client extension to run manual test scenarios

Visual Studio Code's REST Client extension can be used to manually test a sequence of API calls against a locally running FHIR server. This folder is a space where developers can summarize their test scenarios to share with others. Here are steps to use the extension and run the test files in this folder:

1. Install [Visual Studio Code](https://code.visualstudio.com/download)
2. Add the REST Client extension in the Extensions tab in Visual Studio Code or install it [here](https://marketplace.visualstudio.com/items?itemName=humao.rest-client)
3. Run the FHIR server locally
4. Navigate to the `docs\rest` folder, open the `.http` test file you would like to use in Visual Studio Code
5. Click "Send Request" above each HTTP request in the file

Further documentation about how to use the extension can be found on the [REST Client installation page](https://marketplace.visualstudio.com/items?itemName=humao.rest-client).
