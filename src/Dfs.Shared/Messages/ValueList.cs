namespace Dfs.Shared.Messages;

/// <summary>List with value equality, so message records that contain lists compare equal after a JSON round trip.</summary>
/// <typeparam name="T">Element type.</typeparam>
public sealed class ValueList<T> : List<T>, IEquatable<ValueList<T>>
{
    /// <summary>Creates an empty list.</summary>
    public ValueList()
    {
    }

    /// <summary>Creates a list with the given elements.</summary>
    /// <param name="items">Initial elements.</param>
    public ValueList(IEnumerable<T> items) : base(items)
    {
    }

    /// <inheritdoc />
    public bool Equals(ValueList<T>? other) => other is not null && this.SequenceEqual(other);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ValueList<T>);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in this) hash.Add(item);
        return hash.ToHashCode();
    }
}
