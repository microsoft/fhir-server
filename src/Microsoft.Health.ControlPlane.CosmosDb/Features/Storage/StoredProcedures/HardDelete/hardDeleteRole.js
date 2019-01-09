/**
* This stored procedure provides the following functionality:
* - Completely deletes a role and all of its histories.
* 
* @constructor
* @param {string} roleName - Role to be deleted
*/

function hardDeleteRole(roleName) {
    const collection = getContext().getCollection();
    const response = getContext().getResponse();

    const errorMessages = {
        RoleNameNull: `${ErrorCodes.BadRequest}: The roleName is undefined or null.`,
        CannotDeleteAllRows: `${ErrorCodes.BadRequest}: Cannot delete all roles.`,
        RequestEntityTooLarge: `${ErrorCodes.RequestEntityTooLarge}: The request could not be completed.`
    };

    // Validate input
    if (!roleName) {
        throw new Error(errorMessages.RoleNameNull);
    }

    let deletedResourceIdList = new Array();


    validate();

    function validate() {
        // count all resources
        let query = {
            query: "SELECT COUNT(*) FROM ROOT r"
        };

        let isQueryAccepted = collection.queryDocuments(
            collection.getSelfLink(),
            query,
            {},
            function (err, documents, responseOptions) {
                if (err) {
                    throw err;
                }

                if (documents.length > 1) {
                    // Delete the documents.
                    tryQueryAndHardDelete(documents);
                } else {
                    // There is no more documents so we are finished.
                    throw new Error(errorMessage.CannotDeleteAllRows);
                }
            });

        if (!isQueryAccepted) {
            // We ran out of time.
            throw new Error(errorMessage.RequestEntityTooLarge);
        }

    }

    function tryQueryAndHardDelete() {
        // Find the resource and all of its history.
        let query = {
            query: "SELECT r._self, r.id FROM ROOT r WHERE r.id = @roleName",
            parameters: [{ name: "@roleName", value: roleName }]
        };

        let isQueryAccepted = collection.queryDocuments(
            collection.getSelfLink(),
            query,
            {},
            function (err, documents, responseOptions) {
                if (err) {
                    throw err;
                }

                if (documents.length > 0) {
                    // Delete the documents.
                    tryHardDelete(documents);
                } else {
                    // There is no more documents so we are finished.
                    response.setBody(deletedResourceIdList);
                }
            });

        if (!isQueryAccepted) {
            // We ran out of time.
            throw new Error(errorMessage.RequestEntityTooLarge);
        }
    }

    function tryHardDelete(documents) {
        if (documents.length > 0) {
            deletedResourceIdList.push(documents[0].id);

            // Delete the first item.
            var isAccepted = collection.deleteDocument(
                documents[0]._self,
                {},
                function (err, responseOptions) {
                    if (err) {
                        throw err;
                    }

                    // Successfully deleted the item, continue deleting.
                    documents.shift();
                    tryHardDelete(documents);
                });

            if (!isAccepted) {
                // We ran out of time.
                throw new Error(errorMessages.RequestEntityTooLarge);
            }
        } else {
            // If the documents are empty, query for more documents.
            tryQueryAndHardDelete();
        }
    }
}
