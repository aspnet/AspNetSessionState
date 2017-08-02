function GetStateItem(sessionId) {
    var collection = getContext().getCollection();
    var collectionLink = collection.getSelfLink();
    var response = getContext().getResponse();

    if (!sessionId) {
        throw new Error("");
    }

    tryGetStateItem();

    function tryGetStateItem(continuation) {
        var query = 'select * from root r where r.id = "' + sessionId + '"';
        var isAccepted = collection.queryDocuments(collectionLink, query, {},
            function (err, documents, responseOptions) {
                if (err) {
                    throw err;
                }
                if (documents.length > 0) {
                    var doc = documents[0];
                    if (doc.Locked) {
                        doc.SessionItem = null;
                        doc.lockAge = Math.round(((new Date()).getTime() - doc.lockDate.getTime()) / 1000);
                        response.setBody(doc);
                    }
                    else {
                        // using write operation to reset the TTL
                        var isAccepted = collection.replaceDocument(doc._self, doc, requestOptions,
                            function (err, updatedDocument, responseOptions) {
                                if (err) {
                                    throw err;
                                }
                                response.setBody(updatedDocument);
                            });
                        if (!isAccepted) {
                            throw new Error("The SP timed out.");
                        }
                    }
                }
                else if (responseOptions.continuation) {
                    tryGetStateItem(responseOptions.continuation);
                }
                else {
                    throw new Error('Session ' + sessionId + ' not found');
                }
            });

        if (!isAccepted) {
            throw new Error("The SP timed out.");
        }
    }
}


function GetStateItemExclusive(sessionId) {
    var collection = getContext().getCollection();
    var collectionLink = collection.getSelfLink();
    var response = getContext().getResponse();

    if (!sessionId) {
        throw new Error('');
    }

    tryGetStateItem();

    function tryGetStateItemExclusive(continuation) {
        var query = 'select * from root r where r.id = "' + sessionId + '"';
        var isAccepted = collection.queryDocuments(collectionLink, query, {},
            function (err, documents, responseOptions) {
                if (err) {
                    throw err;
                }
                if (documents.length > 0) {
                    var doc = documents[0];
                    if (doc.Locked) {
                        doc.SessionItem = null;
                        doc.lockAge = Math.round(((new Date()).getTime() - doc.lockDate) / 1000);
                        response.setBody(doc);
                    }
                    else {
                        doc.lockAge = 0;
                        doc.lockCookie += 1;
                        doc.Locked = true;
                        var isAccepted = collection.replaceDocument(doc._self, doc, requestOptions,
                            function (err, updatedDocument, responseOptions) {
                                if (err) {
                                    throw err;
                                }
                                response.setBody(updatedDocument);
                            });
                        if (!isAccepted) {
                            throw new Error('The SP timed out.');
                        }
                    }
                }
                else if (responseOptions.continuation) {
                    tryGetStateItem(responseOptions.continuation);
                }
                else {
                    throw new Error('Session ' + sessionId + ' not found');
                }
            });

        if (!isAccepted) {
            throw new Error('The SP timed out.');
        }
    }
}

function ReleaseItemExclusive(sessionId, lockCookie) {
    var collection = getContext().getCollection();
    var collectionLink = collection.getSelfLink();
    var response = getContext().getResponse();

    if (!sessionId) {
        throw new Error('sessionId cannot be null');
    }
    TryReleaseItemExclusive();

    function TryReleaseItemExclusive(continuation) {
        var query = 'select * from root r where r.id = "' + sessionId + '" and lockCookie = ' + lockCookie;
        var isAccepted = collection.queryDocuments(collectionLink, query, {},
            function (err, documents, responseOptions) {
                if (err) {
                    throw err;
                }
                if (documents.length > 0) {
                    var doc = documents[0];
                    doc.Locked = false
                    var isAccepted = collection.replaceDocument(doc._self, doc, requestOptions,
                        function (err, updatedDocument, responseOptions) {
                            if (err) {
                                throw err;
                            }
                            response.setBody({updated: true});
                        });
                    if (!isAccepted) {
                        throw new Error('The SP timed out.');
                    }
                }
                else if (responseOptions.continuation) {
                    TryReleaseItemExclusive(responseOptions.continuation);
                }
                else {
                    response.setBody({ updated: false });
                }
            });

        if (!isAccepted) {
            throw new Error('The SP timed out.');
        }
    }
}

function RemoveStateItem(sessionId, lockCookie) {
    var collection = getContext().getCollection();
    var collectionLink = collection.getSelfLink();
    var response = getContext().getResponse();

    if (!sessionId) {
        throw new Error('sessionId cannot be null');
    }
    if (!lockCookie)
    {
        throw new Error('lockCookie cannot be null');
    }
    TryRemoveStateItem();

    function TryRemoveStateItem(continuation) {
        var query = 'select * from root r where r.id = "' + sessionId + '" and lockCookie = ' + lockCookie;
        var isAccepted = collection.queryDocuments(collectionLink, query, {},
            function (err, documents, responseOptions) {
                if (err) {
                    throw err;
                }
                if (documents.length > 0) {
                    var doc = documents[0];
                    var isAccepted = collection.deleteDocument(doc, requestOptions,
                        function (err, updatedDocument, responseOptions) {
                        if (err) {
                            throw err;
                        }
                        response.setBody({ updated: true });
                    });
                    if (!isAccepted) {
                        throw new Error('The SP timed out.');
                    }
                }
                else if (responseOptions.continuation) {
                    TryReleaseItemExclusive(responseOptions.continuation);
                }
                else {
                    response.setBody({ updated: false });
                }
            });

        if (!isAccepted) {
            throw new Error('The SP timed out.');
        }
    }
}


function CreateUninitializedItem(sessionId, appId, timeout, lockCookie, sessionItem) {
    var collection = getContext().getCollection();
    var collectionLink = collection.getSelfLink();
    var response = getContext().getResponse();

    if (!sessionId) {
        throw new Error('sessionId cannot be null');
    }
    if (!appId) {
        throw new Error('appId cannot be null');
    }
    if (!timeout) {
        throw new Error('timeout cannot be null');
    }
    if (!lockCookie) {
        throw new Error('lockCookie cannot be null');
    }
    var sessionStateItem = { id: sessionId, appId: appid, lockDate: (new Date()).getTime(), lockAge: 0, lockCookie: lockCookie, ttl: timeout, locked: false, sessionItem: sessionItem };
    collection.createDocument(collectionLink, sessionStateItem,
        function (err, documentCreated) {
            if (err) {
                throw err;
            }
        });
}


function ResetItemTimeout(sessionId) {
    var collection = getContext().getCollection();
    var collectionLink = collection.getSelfLink();
    var response = getContext().getResponse();

    if (!sessionId) {
        throw new Error('');
    }

    tryResetItemTimeout();

    function tryResetItemTimeout(continuation) {
        var query = 'select * from root r where r.id = "' + sessionId + '"';
        var isAccepted = collection.queryDocuments(collectionLink, query, {},
            function (err, documents, responseOptions) {
                if (err) {
                    throw err;
                }
                if (documents.length > 0) {
                    var doc = documents[0];
                    // using write operation to reset the TTL
                    var isAccepted = collection.replaceDocument(doc._self, doc, requestOptions,
                        function (err, updatedDocument, responseOptions) {
                            if (err) {
                                throw err;
                            }
                            response.setBody({ updated: true });
                        });
                    if (!isAccepted) {
                        throw new Error('The SP timed out.');
                    }
                }
                else if (responseOptions.continuation) {
                    tryGetStateItem(responseOptions.continuation);
                }
                else {
                    response.setBody({ updated: false });
                }
            });

        if (!isAccepted) {
            throw new Error('The SP timed out.');
        }
    }
}

