

using Microsoft.JSInterop;

namespace IndexedDB.EntityFrameworkCore;

internal class Example
{
    public Guid Id { get; set; }

    public string? Name { get; set; }
}

internal class ExampleContext : IndexedDbContext
{
    public ExampleContext(IJSRuntime jsRuntime, 
        string databaseName = "ExampleDB", 
        int version = 1)
        : base(jsRuntime, databaseName, version)
    {

    }

    public IndexedDbSet<Example>? Examples { get; set; }
}
