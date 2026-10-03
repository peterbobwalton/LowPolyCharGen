namespace LowPolyCharGen.Fbx;

/// <summary>
/// One record of an FBX document: a name, a list of properties and child records.
/// Supported property types: bool, short, int, long, float, double, string, byte[] and
/// arrays of int, long, float, double.
/// </summary>
public sealed class FbxNode(string name, params object[] properties)
{
    public string Name { get; } = name;
    public List<object> Properties { get; } = [.. properties];
    public List<FbxNode> Children { get; } = [];

    /// <summary>Adds a child record and returns it.</summary>
    public FbxNode Add(string name, params object[] properties)
    {
        var child = new FbxNode(name, properties);
        Children.Add(child);
        return child;
    }

    /// <summary>Adds a "P" record to a Properties70 node.</summary>
    public FbxNode P(string name, string type, string label, string flags, params object[] values)
    {
        var child = new FbxNode("P", name, type, label, flags);
        child.Properties.AddRange(values);
        Children.Add(child);
        return child;
    }
}
