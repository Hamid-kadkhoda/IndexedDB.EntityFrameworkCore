using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.JSInterop;

namespace IndexedDB.EntityFrameworkCore;

public class IndexedDbSet<TEntity> where TEntity : class
{
    private string _storeName;

    private List<TEntity> Added { get; set; } = [];

    private List<TEntity> Modified { get; set; } = [];

    private List<TEntity> Deleted { get; set; } = [];

    private IJSObjectReference? _module;

    public IndexedDbSet(string storeName)
    {
        _storeName = storeName;
    }

    internal void SetModule(IJSObjectReference module)
    {
        _module = module;
    }

    // Add entity
    public void Add(TEntity entity)
    {
        Added.Add(entity);
    }

    public void AddRange(IEnumerable<TEntity> entities)
    {
        Added.AddRange(entities);
    }

    // Update entity
    public void Update(TEntity entity)
    {
        Modified.Add(entity);
    }

    // Remove entity
    public void Remove(TEntity entity)
    {
        Deleted.Add(entity);
    }

    public void RemoveRange(IEnumerable<TEntity> entities)
    {
        Deleted.AddRange(entities);
    }

    // Find by ID
    public async Task<TEntity?> FindAsync(object id)
    {
        if (_module == null) throw new InvalidOperationException("IndexedDbSet not initialized");

        var json = await _module.InvokeAsync<string>(IndexedDbContext_Consts.GetRecord, _storeName, id);
        return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<TEntity>(json);
    }

    // ToList equivalent
    public async Task<List<TEntity>> ToListAsync()
    {
        if (_module == null) throw new InvalidOperationException("IndexedDbSet not initialized");

        var json = await _module.InvokeAsync<string>(IndexedDbContext_Consts.GetAllRecords, _storeName);
        return JsonSerializer.Deserialize<List<TEntity>>(json) ?? new List<TEntity>();
    }

    // FirstOrDefault equivalent
    public async Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate)
    {
        var all = await ToListAsync();
        return all.AsQueryable().FirstOrDefault(predicate);
    }

    // Where equivalent
    public async Task<List<TEntity>> Where(Expression<Func<TEntity, bool>> predicate)
    {
        var all = await ToListAsync();
        return [.. all.AsQueryable().Where(predicate)];
    }

    // Any equivalent
    public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>>? predicate = null)
    {
        var all = await ToListAsync();
        return predicate == null ? all.Count != 0 : all.AsQueryable().Any(predicate);
    }

    // Count equivalent
    public async Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null)
    {
        var all = await ToListAsync();
        return predicate == null ? all.Count : all.AsQueryable().Count(predicate);
    }

    // Save changes for this IndexedDbSet
    public async Task<int> SaveChangesAsync(IJSObjectReference module)
    {
        _module = module;
        int changeCount = 0;

        // Add
        foreach (var entity in Added)
        {
            var json = JsonSerializer.Serialize(entity);
            await module.InvokeVoidAsync(IndexedDbContext_Consts.AddRecord, _storeName, json);
            changeCount++;
        }

        // Update
        foreach (var entity in Modified)
        {
            var json = JsonSerializer.Serialize(entity);
            await module.InvokeVoidAsync(IndexedDbContext_Consts.UpdateRecord, _storeName, json);
            changeCount++;
        }

        // Delete
        foreach (var entity in Deleted)
        {
            var idProp = typeof(TEntity).GetProperty("Id");
            if (idProp != null)
            {
                var id = idProp.GetValue(entity);
                await module.InvokeVoidAsync(IndexedDbContext_Consts.DeleteRecord, _storeName, id);
                changeCount++;
            }
        }

        Added.Clear();
        Modified.Clear();
        Deleted.Clear();

        return changeCount;
    }
}
