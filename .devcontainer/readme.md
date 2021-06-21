## DevContainers

[DevContainers](https://code.visualstudio.com/docs/remote/containers) lets you use a Docker container as a full-featured development environment. It allows you to open any folder inside (or mounted into) a container and take advantage of Visual Studio Code's full feature set.

### Prerequisites

Visual Studio Code

### Usage

1. Install the 'Visual Studio Code Remote - Containers' extension.
2. vs code will ask to open the folder inside a container. allow it.
3. Your dev env is ready for use.

### Running / Debugging FHIR

1. Update [launch.json](../.vscode/launch.json) file, under the correct profile add the connection string to the data store
2. Run the selected profile
3. Use postman to query the server. e.g. https://localhost:44348/Patient