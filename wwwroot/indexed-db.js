// indexed-db.js - Place in wwwroot folder or _content/YourProject/
let db = null;
let dbName = null;
let dbVersion = null;
let log = false;

export function initDatabase(databaseName, version, stores) {
    return new Promise((resolve, reject) => {
        dbName = databaseName;
        dbVersion = version;

        if(log) console.log(`Initializing IndexedDB: ${databaseName} v${version}`);
        if(log) console.log('Stores to create:', stores);

        const request = indexedDB.open(databaseName, version);

        request.onerror = (event) => {
            if(log) console.error('Database error:', event.target.error);
            reject(event.target.error);
        };

        request.onsuccess = (event) => {
            db = event.target.result;
            if(log) console.log('Database opened successfully');
            if(log) console.log('Available stores:', Array.from(db.objectStoreNames));
            resolve();
        };

        request.onupgradeneeded = (event) => {
            if(log) console.log('Database upgrade needed');
            db = event.target.result;

            stores.forEach(store => {
                // Delete existing store if it exists (for schema changes)
                if (db.objectStoreNames.contains(store.name)) {
                    console.log(`Deleting existing store: ${store.name}`);
                    db.deleteObjectStore(store.name);
                }

                // Create new store
                if(log) console.log(`Creating store: ${store.name} with keyPath: ${store.keyPath}`);
                const objectStore = db.createObjectStore(store.name, {
                    keyPath: store.keyPath,
                    autoIncrement: store.autoIncrement
                });
                
                if(log) console.log(`Store ${store.name} created successfully`);
            });
        };

        request.onblocked = () => {
            if(log) console.warn('Database upgrade blocked. Close other tabs using this database.');
        };
    });
}

function ensureDatabase() {
    if (!db) {
        throw new Error('Database not initialized. Call initDatabase first.');
    }
}

function validateStore(storeName) {
    if (!db.objectStoreNames.contains(storeName)) {
        const available = Array.from(db.objectStoreNames).join(', ');
        throw new Error(
            `Store "${storeName}" not found. Available stores: ${available || 'none'}. ` +
            `Did you call SaveChangesAsync to initialize the database?`
        );
    }
}

export function addRecord(storeName, jsonData) {
    return new Promise((resolve, reject) => {
        try {
            ensureDatabase();
            validateStore(storeName);

            const data = JSON.parse(jsonData);
            if(log) console.log(`Adding record to ${storeName}:`, data);

            const transaction = db.transaction([storeName], 'readwrite');
            const store = transaction.objectStore(storeName);
            const request = store.add(data);

            request.onsuccess = () => {
                if(log) console.log(`Record added with key: ${request.result}`);
                resolve(request.result);
            };

            request.onerror = () => {
                if(log) console.error(`Error adding record to ${storeName}:`, request.error);
                reject(request.error);
            };

            transaction.onerror = () => {
                if(log) console.error(`Transaction error for ${storeName}:`, transaction.error);
                reject(transaction.error);
            };
        } catch (error) {
            if(log) console.error('Error in addRecord:', error);
            reject(error);
        }
    });
}

export function updateRecord(storeName, jsonData) {
    return new Promise((resolve, reject) => {
        try {
            ensureDatabase();
            validateStore(storeName);

            const data = JSON.parse(jsonData);
            if(log) console.log(`Updating record in ${storeName}:`, data);

            const transaction = db.transaction([storeName], 'readwrite');
            const store = transaction.objectStore(storeName);
            const request = store.put(data);

            request.onsuccess = () => {
                if(log) console.log(`Record updated with key: ${request.result}`);
                resolve(request.result);
            };

            request.onerror = () => {
                if(log) console.error(`Error updating record in ${storeName}:`, request.error);
                reject(request.error);
            };
        } catch (error) {
            if(log) console.error('Error in updateRecord:', error);
            reject(error);
        }
    });
}

export function deleteRecord(storeName, id) {
    return new Promise((resolve, reject) => {
        try {
            ensureDatabase();
            validateStore(storeName);

            if(log) console.log(`Deleting record from ${storeName} with id:`, id);

            const transaction = db.transaction([storeName], 'readwrite');
            const store = transaction.objectStore(storeName);
            const request = store.delete(id);

            request.onsuccess = () => {
                if(log) console.log(`Record deleted from ${storeName}`);
                resolve();
            };

            request.onerror = () => {
                if(log) console.error(`Error deleting record from ${storeName}:`, request.error);
                reject(request.error);
            };
        } catch (error) {
            if(log) console.error('Error in deleteRecord:', error);
            reject(error);
        }
    });
}

export function getRecord(storeName, id) {
    return new Promise((resolve, reject) => {
        try {
            ensureDatabase();
            validateStore(storeName);

            if(log) console.log(`Getting record from ${storeName} with id:`, id);

            const transaction = db.transaction([storeName], 'readonly');
            const store = transaction.objectStore(storeName);
            const request = store.get(id);

            request.onsuccess = () => {
                const result = request.result ? JSON.stringify(request.result) : null;
                if(log) console.log(`Record retrieved from ${storeName}:`, result);
                resolve(result);
            };

            request.onerror = () => {
                if(log) console.error(`Error getting record from ${storeName}:`, request.error);
                reject(request.error);
            };
        } catch (error) {
            if(log) console.error('Error in getRecord:', error);
            reject(error);
        }
    });
}

export function getAllRecords(storeName) {
    return new Promise((resolve, reject) => {
        try {
            ensureDatabase();
            validateStore(storeName);

            if(log) console.log(`Getting all records from ${storeName}`);

            const transaction = db.transaction([storeName], 'readonly');
            const store = transaction.objectStore(storeName);
            const request = store.getAll();

            request.onsuccess = () => {
                const result = JSON.stringify(request.result || []);
                if(log) console.log(`Retrieved ${request.result?.length || 0} records from ${storeName}`);
                resolve(result);
            };

            request.onerror = () => {
                if(log) console.error(`Error getting all records from ${storeName}:`, request.error);
                reject(request.error);
            };
        } catch (error) {
            if(log) console.error('Error in getAllRecords:', error);
            reject(error);
        }
    });
}

export function clearStore(storeName) {
    return new Promise((resolve, reject) => {
        try {
            ensureDatabase();
            validateStore(storeName);

            if(log) console.log(`Clearing store: ${storeName}`);

            const transaction = db.transaction([storeName], 'readwrite');
            const store = transaction.objectStore(storeName);
            const request = store.clear();

            request.onsuccess = () => {
                if(log) console.log(`Store ${storeName} cleared`);
                resolve();
            };

            request.onerror = () => {
                if(log) console.error(`Error clearing store ${storeName}:`, request.error);
                reject(request.error);
            };
        } catch (error) {
            if(log) console.error('Error in clearStore:', error);
            reject(error);
        }
    });
}

// Utility function to check database status
export function getDatabaseInfo() {
    if (!db) {
        return { initialized: false };
    }
    return {
        initialized: true,
        name: db.name,
        version: db.version,
        stores: Array.from(db.objectStoreNames)
    };
}