using Microsoft.JSInterop;

namespace IndexedDB.EntityFrameworkCore;

public abstract class IndexedDbContext : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private IJSObjectReference? _module;
    private readonly string _databaseName;
    private readonly int _version;
    private bool _isInitialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    protected IndexedDbContext(IJSRuntime jsRuntime, string databaseName, int version = 1)
    {
        _jsRuntime = jsRuntime;
        _databaseName = databaseName;
        _version = version;
    }

    // Initialize the database - CRITICAL: Must complete before any operations
    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            _module = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", IndexedDbContext_Consts.Js_path);

            var stores = GetStoreDefinitions();

            // Wait for database to be fully initialized
            await _module.InvokeVoidAsync(IndexedDbContext_Consts.InitDatabase, _databaseName, _version, stores);

            // Initialize all DbSet modules
            foreach (var prop in GetType().GetProperties())
            {
                if (prop.PropertyType.IsGenericType &&
                    prop.PropertyType.GetGenericTypeDefinition() == typeof(IndexedDbSet<>))
                {
                    var dbSet = prop.GetValue(this);
                    if (dbSet != null)
                    {
                        var setModuleMethod = prop.PropertyType.GetMethod(nameof(IndexedDbSet<object>.SetModule));
                        setModuleMethod?.Invoke(dbSet, new object[] { _module });
                    }
                }
            }

            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    // Get all DbSet properties
    private List<StoreDefinition> GetStoreDefinitions()
    {
        var stores = new List<StoreDefinition>();
        var dbSetType = typeof(IndexedDbSet<>);

        foreach (var prop in GetType().GetProperties())
        {
            if (prop.PropertyType.IsGenericType &&
                prop.PropertyType.GetGenericTypeDefinition() == dbSetType)
            {
                var entityType = prop.PropertyType.GetGenericArguments()[0];
                var keyProperty = entityType.GetProperties()
                    .FirstOrDefault(p => p.Name == "Id" ||
                                       p.GetCustomAttributes(typeof(KeyAttribute), true).Any());

                stores.Add(new StoreDefinition
                {
                    Name = prop.Name,
                    KeyPath = keyProperty?.Name ?? "Id",
                    AutoIncrement = keyProperty?.PropertyType == typeof(int) || keyProperty?.PropertyType == typeof(long)
                });
            }
        }

        return stores;
    }

    // SaveChanges equivalent
    public async Task<int> SaveChangesAsync()
    {
        await EnsureInitializedAsync();

        if (_module == null)
            throw new InvalidOperationException("Database module not initialized");

        int changeCount = 0;

        try
        {
            foreach (var prop in GetType().GetProperties())
            {
                if (prop.PropertyType.IsGenericType &&
                    prop.PropertyType.GetGenericTypeDefinition() == typeof(IndexedDbSet<>))
                {
                    var dbSet = prop.GetValue(this);
                    if (dbSet != null)
                    {
                        var saveMethod = prop.PropertyType.GetMethod("SaveChangesAsync");
                        if (saveMethod != null)
                        {
                            var task = (Task<int>)saveMethod.Invoke(dbSet, new object[] { _module })!;
                            changeCount += await task;
                        }
                    }
                }
            }
        }
        catch (System.Runtime.InteropServices.JavaScript.JSException jsEx)
        {
            throw new InvalidOperationException(
                $"Failed to save changes. Ensure database is initialized. Error: {jsEx.Message}", jsEx);
        }

        return changeCount;
    }

    public async ValueTask DisposeAsync()
    {
        _initLock?.Dispose();
        if (_module != null)
        {
            await _module.DisposeAsync();
        }
    }
}