
namespace IndexedDB.EntityFrameworkCore;

[AttributeUsage(AttributeTargets.Property)]
public class IndexedTableAttribute(string name) : Attribute
{
    public string Name { get; set; } = name;
}