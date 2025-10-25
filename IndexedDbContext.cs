using Microsoft.JSInterop;

namespace IndexedDB.EntityFrameworkCore;

public abstract class IndexedDbContext : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private IJSObjectReference? _module;
    private readonly string _databaseName;
    private readonly int _version;

    protected IndexedDbContext(IJSRuntime jsRuntime, string databaseName, int version = 1)
    {
        _jsRuntime = jsRuntime;
        _databaseName = databaseName;
        _version = version;

        InitializeSets().Wait();
    }

    private async Task InitializeSets()
    {
        _module = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", IndexedDbContext_Consts.Js_path);

        var stores = GetStoreDefinitions();

        await _module.InvokeVoidAsync(IndexedDbContext_Consts.InitDatabase, _databaseName, _version, stores);

        var props = GetType()
            .GetProperties()
            .Where(prop => prop.PropertyType.IsGenericType
                && prop.PropertyType.GetGenericTypeDefinition() == typeof(IndexedDbSet<>));

        foreach (var prop in props)
        {
            var setModuleMethod = prop.PropertyType.GetMethod(nameof(IndexedDbSet<object>.SetModule));
            var setStoreMethod = prop.PropertyType.GetMethod(nameof(IndexedDbSet<object>.SetStoreName));

            var dbSet = prop.GetValue(this);
            if (dbSet == null)
            {
                dbSet = Activator.CreateInstance(prop.PropertyType);
                prop.SetValue(this, dbSet);
            }

            setModuleMethod?.Invoke(dbSet, [_module]);
            setStoreMethod?.Invoke(dbSet, [prop.Name]);
        }
    }

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

    public async Task<int> SaveChangesAsync()
    {
        if (_module == null)
            throw new InvalidOperationException("Database module not initialized");

        int changeCount = 0;

        try
        {
            var props = GetType()
                        .GetProperties()
                        .Where(prop => prop.PropertyType.IsGenericType
                    && prop.PropertyType.GetGenericTypeDefinition() == typeof(IndexedDbSet<>));

            foreach (var prop in props)
            {
                var dbSet = prop.GetValue(this);
                if (dbSet != null)
                {
                    var saveMethod = prop.PropertyType.GetMethod(nameof(IndexedDbSet<object>.SaveChangesAsync));
                    if (saveMethod != null)
                    {
                        var task = (Task<int>)saveMethod.Invoke(dbSet, [_module])!;
                        changeCount += await task;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save changes. Ensure database is initialized. Error: {ex.Message}", ex);
        }

        return changeCount;
    }

    public async ValueTask DisposeAsync()
    {
        if (_module != null)
        {
            await _module.DisposeAsync();
        }
    }
}