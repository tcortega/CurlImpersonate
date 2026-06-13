namespace CurlImpersonate.Http.Internal;

internal static class HeaderValidation
{
    public static bool IsValidHeaderName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (var ch in name)
        {
            if (!IsTokenChar(ch))
                return false;
        }

        return true;
    }

    private static bool IsTokenChar(char ch)
    {
        return ch is >= 'A' and <= 'Z' ||
               ch is >= 'a' and <= 'z' ||
               ch is >= '0' and <= '9' ||
               ch is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
    }
}
