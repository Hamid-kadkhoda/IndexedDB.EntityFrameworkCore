namespace IndexedDB.EntityFrameworkCore;

internal class StoreDefinition
{
    public string Name { get; set; } = "";

    public string KeyPath { get; set; } = "";

    public bool AutoIncrement { get; set; }
}