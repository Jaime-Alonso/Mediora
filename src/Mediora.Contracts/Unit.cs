namespace Mediora;

/// <summary>
/// Represents a void value for requests that do not return meaningful data.
/// </summary>
public readonly struct Unit : IEquatable<Unit>
{
    /// <summary>
    /// Gets the singleton value of <see cref="Unit"/>.
    /// </summary>
    public static readonly Unit Value = new();

    /// <summary>
    /// Gets a completed task containing <see cref="Value"/>.
    /// </summary>
    public static Task<Unit> Task { get; } = System.Threading.Tasks.Task.FromResult(Value);

    /// <summary>
    /// Determines whether this instance equals another <see cref="Unit"/> value.
    /// </summary>
    /// <param name="other">The value to compare.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public bool Equals(Unit other)
    {
        return true;
    }

    /// <summary>
    /// Determines whether this instance equals another object.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns><see langword="true"/> when <paramref name="obj"/> is a <see cref="Unit"/> value.</returns>
    public override bool Equals(object? obj)
    {
        return obj is Unit;
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>Always <c>0</c>.</returns>
    public override int GetHashCode()
    {
        return 0;
    }

    /// <summary>
    /// Returns the string representation of this value.
    /// </summary>
    /// <returns>The empty tuple representation <c>()</c>.</returns>
    public override string ToString()
    {
        return "()";
    }

    /// <summary>
    /// Determines whether two <see cref="Unit"/> values are equal.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public static bool operator ==(Unit left, Unit right)
    {
        return true;
    }

    /// <summary>
    /// Determines whether two <see cref="Unit"/> values are not equal.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public static bool operator !=(Unit left, Unit right)
    {
        return false;
    }
}
