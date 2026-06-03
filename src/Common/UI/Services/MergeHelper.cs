namespace Stratum.Common.UI.Services;

/// <summary>
/// Utility for 3-way merge of form fields during concurrent editing.
/// Reduces the per-field merge pattern to a single line.
/// </summary>
public static class MergeHelper
{
    /// <summary>
    /// 3-way merge of a string field. Returns true if conflict detected.
    /// <list type="bullet">
    /// <item>If <paramref name="local"/> == <paramref name="original"/> → accept server value (no local edit)</item>
    /// <item>If <paramref name="server"/> == <paramref name="original"/> → keep local value (no server edit)</item>
    /// <item>If both differ from original → conflict</item>
    /// </list>
    /// Always updates <paramref name="original"/> to <paramref name="server"/> for the next merge cycle.
    /// Works for both nullable and non-nullable strings.
    /// </summary>
    public static bool Merge(ref string? local, ref string? original, string? server)
    {
        var conflict = false;

        if (local == original)
        {
            local = server;
        }
        else if (server != original)
        {
            conflict = true;
        }

        original = server;
        return conflict;
    }
}
