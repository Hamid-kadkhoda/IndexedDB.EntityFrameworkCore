using Xunit;
using Microsoft.JSInterop;
using System.Text.Json;

namespace IndexedDB.EntityFrameworkCore;

// Test entities
public class TestPerson
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Email { get; set; } = "";
}

public class TestProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

// Test context
public class Test_IndexedDbContext : IndexedDbContext
{
    public Test_IndexedDbContext(IJSRuntime jsRuntime) 
        : base(jsRuntime, "TestDatabase", version: 1)
    {
        People = new IndexedDbSet<TestPerson>("People");
        Products = new IndexedDbSet<TestProduct>("Products");
    }

    public IndexedDbSet<TestPerson> People { get; set; }
    public IndexedDbSet<TestProduct> Products { get; set; }
}

// Mock JSRuntime for testing
public class MockJSRuntime : IJSRuntime
{
    private readonly Dictionary<string, Dictionary<object, string>> _stores = new();
    private int _nextId = 1;

    public MockJSRuntime()
    {
        _stores["People"] = new Dictionary<object, string>();
        _stores["Products"] = new Dictionary<object, string>();
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        return InvokeAsync<TValue>(identifier, default, args);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        try
        {
            // Handle import
            if (identifier == "import")
            {
                return new ValueTask<TValue>((TValue)(object)new MockJSObjectReference(this));
            }

            return default;
        }
        catch
        {
            return default;
        }
    }
}

public class MockJSObjectReference : IJSObjectReference
{
    private readonly MockJSRuntime _runtime;
    private readonly Dictionary<string, Dictionary<object, string>> _stores = new();
    private int _nextId = 1;

    public MockJSObjectReference(MockJSRuntime runtime)
    {
        _runtime = runtime;
        _stores["People"] = new Dictionary<object, string>();
        _stores["Products"] = new Dictionary<object, string>();
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        return InvokeAsync<TValue>(identifier, default, args);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        args ??= Array.Empty<object>();

        switch (identifier)
        {
            case "initDatabase":
                return new ValueTask<TValue>(default(TValue)!);

            case "addRecord":
                {
                    var storeName = args[0]?.ToString() ?? "";
                    var jsonData = args[1]?.ToString() ?? "";
                    var data = JsonSerializer.Deserialize<JsonElement>(jsonData);
                    
                    var id = _nextId++;
                    var dataWithId = JsonSerializer.Serialize(new
                    {
                        Id = id,
                        Name = data.TryGetProperty("Name", out var name) ? name.GetString() : "",
                        Age = data.TryGetProperty("Age", out var age) ? age.GetInt32() : 0,
                        Email = data.TryGetProperty("Email", out var email) ? email.GetString() : "",
                        Price = data.TryGetProperty("Price", out var price) ? price.GetDecimal() : 0
                    });

                    _stores[storeName][id] = dataWithId;
                    return new ValueTask<TValue>((TValue)(object)id);
                }

            case "updateRecord":
                {
                    var storeName = args[0]?.ToString() ?? "";
                    var jsonData = args[1]?.ToString() ?? "";
                    var data = JsonSerializer.Deserialize<JsonElement>(jsonData);
                    var id = data.GetProperty("Id").GetInt32();
                    
                    _stores[storeName][id] = jsonData;
                    return new ValueTask<TValue>(default(TValue)!);
                }

            case "deleteRecord":
                {
                    var storeName = args[0]?.ToString() ?? "";
                    var id = args[1];
                    _stores[storeName].Remove(id!);
                    return new ValueTask<TValue>(default(TValue)!);
                }

            case "getRecord":
                {
                    var storeName = args[0]?.ToString() ?? "";
                    var id = args[1];
                    
                    if (_stores[storeName].TryGetValue(id!, out var json))
                    {
                        return new ValueTask<TValue>((TValue)(object)json);
                    }
                    return new ValueTask<TValue>((TValue)(object)string.Empty);
                }

            case "getAllRecords":
                {
                    var storeName = args[0]?.ToString() ?? "";
                    var allRecords = _stores[storeName].Values.ToList();
                    var json = JsonSerializer.Serialize(allRecords.Select(r => JsonSerializer.Deserialize<object>(r)));
                    return new ValueTask<TValue>((TValue)(object)json);
                }

            case "clearStore":
                {
                    var storeName = args[0]?.ToString() ?? "";
                    _stores[storeName].Clear();
                    return new ValueTask<TValue>(default(TValue)!);
                }

            default:
                return new ValueTask<TValue>(default(TValue)!);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

// Unit Tests
public class IndexedDbContextTests : IDisposable
{
    private readonly MockJSRuntime _jsRuntime;
    private readonly Test_IndexedDbContext _context;

    public IndexedDbContextTests()
    {
        _jsRuntime = new MockJSRuntime();
        _context = new Test_IndexedDbContext(_jsRuntime);
    }

    [Fact]
    public async Task Add_ShouldAddEntityToDatabase()
    {
        // Arrange
        var person = new TestPerson
        {
            Name = "John Doe",
            Age = 30,
            Email = "john@example.com"
        };

        // Act
        _context.People.Add(person);
        var changes = await _context.SaveChangesAsync();

        // Assert
        Assert.Equal(1, changes);
        var people = await _context.People.ToListAsync();
        Assert.Single(people);
        Assert.Equal("John Doe", people[0].Name);
    }

    [Fact]
    public async Task AddRange_ShouldAddMultipleEntities()
    {
        // Arrange
        var people = new[]
        {
            new TestPerson { Name = "John", Age = 30, Email = "john@test.com" },
            new TestPerson { Name = "Jane", Age = 25, Email = "jane@test.com" },
            new TestPerson { Name = "Bob", Age = 35, Email = "bob@test.com" }
        };

        // Act
        _context.People.AddRange(people);
        var changes = await _context.SaveChangesAsync();

        // Assert
        Assert.Equal(3, changes);
        var result = await _context.People.ToListAsync();
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task Update_ShouldModifyExistingEntity()
    {
        // Arrange
        var person = new TestPerson { Name = "John", Age = 30, Email = "john@test.com" };
        _context.People.Add(person);
        await _context.SaveChangesAsync();

        var people = await _context.People.ToListAsync();
        var savedPerson = people.First();

        // Act
        savedPerson.Age = 31;
        savedPerson.Name = "John Updated";
        _context.People.Update(savedPerson);
        var changes = await _context.SaveChangesAsync();

        // Assert
        Assert.Equal(1, changes);
        var updated = await _context.People.FindAsync(savedPerson.Id);
        Assert.NotNull(updated);
        Assert.Equal(31, updated.Age);
        Assert.Equal("John Updated", updated.Name);
    }

    [Fact]
    public async Task Remove_ShouldDeleteEntity()
    {
        // Arrange
        var person = new TestPerson { Name = "John", Age = 30, Email = "john@test.com" };
        _context.People.Add(person);
        await _context.SaveChangesAsync();

        var people = await _context.People.ToListAsync();
        var savedPerson = people.First();

        // Act
        _context.People.Remove(savedPerson);
        var changes = await _context.SaveChangesAsync();

        // Assert
        Assert.Equal(1, changes);
        var result = await _context.People.ToListAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_ShouldReturnEntityById()
    {
        // Arrange
        var person = new TestPerson { Name = "John", Age = 30, Email = "john@test.com" };
        _context.People.Add(person);
        await _context.SaveChangesAsync();

        var people = await _context.People.ToListAsync();
        var id = people.First().Id;

        // Act
        var found = await _context.People.FindAsync(id);

        // Assert
        Assert.NotNull(found);
        Assert.Equal("John", found.Name);
        Assert.Equal(30, found.Age);
    }

    [Fact]
    public async Task FindAsync_ShouldReturnNullForNonExistentId()
    {
        // Act
        var found = await _context.People.FindAsync(999);

        // Assert
        Assert.Null(found);
    }

    [Fact]
    public async Task ToListAsync_ShouldReturnAllEntities()
    {
        // Arrange
        _context.People.AddRange(new[]
        {
            new TestPerson { Name = "John", Age = 30, Email = "john@test.com" },
            new TestPerson { Name = "Jane", Age = 25, Email = "jane@test.com" }
        });
        await _context.SaveChangesAsync();

        // Act
        var result = await _context.People.ToListAsync();

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Where_ShouldFilterEntities()
    {
        // Arrange
        _context.People.AddRange(new[]
        {
            new TestPerson { Name = "John", Age = 30, Email = "john@test.com" },
            new TestPerson { Name = "Jane", Age = 25, Email = "jane@test.com" },
            new TestPerson { Name = "Bob", Age = 35, Email = "bob@test.com" }
        });
        await _context.SaveChangesAsync();

        // Act
        var adults = await _context.People.Where(p => p.Age >= 30);

        // Assert
        Assert.Equal(2, adults.Count);
        Assert.All(adults, p => Assert.True(p.Age >= 30));
    }

    [Fact]
    public async Task FirstOrDefaultAsync_ShouldReturnFirstMatch()
    {
        // Arrange
        _context.People.AddRange(new[]
        {
            new TestPerson { Name = "John", Age = 30, Email = "john@test.com" },
            new TestPerson { Name = "Jane", Age = 25, Email = "jane@test.com" }
        });
        await _context.SaveChangesAsync();

        // Act
        var result = await _context.People.FirstOrDefaultAsync(p => p.Name == "Jane");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Jane", result.Name);
        Assert.Equal(25, result.Age);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_ShouldReturnNullWhenNoMatch()
    {
        // Arrange
        _context.People.Add(new TestPerson { Name = "John", Age = 30, Email = "john@test.com" });
        await _context.SaveChangesAsync();

        // Act
        var result = await _context.People.FirstOrDefaultAsync(p => p.Name == "NonExistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task AnyAsync_ShouldReturnTrueWhenEntitiesExist()
    {
        // Arrange
        _context.People.Add(new TestPerson { Name = "John", Age = 30, Email = "john@test.com" });
        await _context.SaveChangesAsync();

        // Act
        var exists = await _context.People.AnyAsync();
        var hasAdults = await _context.People.AnyAsync(p => p.Age >= 18);

        // Assert
        Assert.True(exists);
        Assert.True(hasAdults);
    }

    [Fact]
    public async Task AnyAsync_ShouldReturnFalseWhenNoEntitiesExist()
    {
        // Act
        var exists = await _context.People.AnyAsync();

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task CountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        _context.People.AddRange(new[]
        {
            new TestPerson { Name = "John", Age = 30, Email = "john@test.com" },
            new TestPerson { Name = "Jane", Age = 25, Email = "jane@test.com" },
            new TestPerson { Name = "Bob", Age = 17, Email = "bob@test.com" }
        });
        await _context.SaveChangesAsync();

        // Act
        var total = await _context.People.CountAsync();
        var adults = await _context.People.CountAsync(p => p.Age >= 18);

        // Assert
        Assert.Equal(3, total);
        Assert.Equal(2, adults);
    }

    [Fact]
    public async Task MultipleDbSets_ShouldWorkIndependently()
    {
        // Arrange
        var person = new TestPerson { Name = "John", Age = 30, Email = "john@test.com" };
        var product = new TestProduct { Name = "Laptop", Price = 999.99m };

        // Act
        _context.People.Add(person);
        _context.Products.Add(product);
        var changes = await _context.SaveChangesAsync();

        // Assert
        Assert.Equal(2, changes);
        var people = await _context.People.ToListAsync();
        var products = await _context.Products.ToListAsync();
        Assert.Single(people);
        Assert.Single(products);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldHandleMultipleOperations()
    {
        // Arrange
        _context.People.AddRange(new[]
        {
            new TestPerson { Name = "John", Age = 30, Email = "john@test.com" },
            new TestPerson { Name = "Jane", Age = 25, Email = "jane@test.com" }
        });
        await _context.SaveChangesAsync();

        var people = await _context.People.ToListAsync();
        var john = people.First(p => p.Name == "John");
        var jane = people.First(p => p.Name == "Jane");

        // Act
        john.Age = 31; // Update
        _context.People.Update(john);
        _context.People.Remove(jane); // Delete
        _context.People.Add(new TestPerson { Name = "Bob", Age = 35, Email = "bob@test.com" }); // Add
        
        var changes = await _context.SaveChangesAsync();

        // Assert
        Assert.Equal(3, changes); // 1 update + 1 delete + 1 add
        var result = await _context.People.ToListAsync();
        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Name == "John" && p.Age == 31);
        Assert.Contains(result, p => p.Name == "Bob");
        Assert.DoesNotContain(result, p => p.Name == "Jane");
    }

    [Fact]
    public async Task RemoveRange_ShouldDeleteMultipleEntities()
    {
        // Arrange
        _context.People.AddRange(new[]
        {
            new TestPerson { Name = "John", Age = 30, Email = "john@test.com" },
            new TestPerson { Name = "Jane", Age = 25, Email = "jane@test.com" },
            new TestPerson { Name = "Bob", Age = 35, Email = "bob@test.com" }
        });
        await _context.SaveChangesAsync();

        var toDelete = await _context.People.Where(p => p.Age >= 30);

        // Act
        _context.People.RemoveRange(toDelete);
        var changes = await _context.SaveChangesAsync();

        // Assert
        Assert.Equal(2, changes);
        var remaining = await _context.People.ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("Jane", remaining[0].Name);
    }

    public void Dispose()
    {
        _context?.DisposeAsync().AsTask().Wait();
    }
}

// Test runner example (for console output)
public class TestRunner
{
    public static async Task RunAllTests()
    {
        var testClass = new IndexedDbContextTests();
        var methods = typeof(IndexedDbContextTests)
            .GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(FactAttribute), false).Any());

        int passed = 0;
        int failed = 0;

        foreach (var method in methods)
        {
            try
            {
                var task = (Task)method.Invoke(testClass, null)!;
                await task;
                Console.WriteLine($"✓ {method.Name}");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ {method.Name}: {ex.InnerException?.Message ?? ex.Message}");
                failed++;
            }
        }

        Console.WriteLine($"\nResults: {passed} passed, {failed} failed");
        testClass.Dispose();
    }
}