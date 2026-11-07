using Mneme.Ingest.Validation;

namespace Mneme.Code;

internal static class CodeInput
{
    internal static bool IsWorkstream(string? value) =>
        WorkstreamIdValidator.IsValid(value) && !value!.Any(char.IsControl);

    internal static void Repository(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A host-assigned repository identity is required.", nameof(value));
    }

    internal static void Revision(string value)
    {
        if (value is null || value.Length is not (40 or 64) ||
            value.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Revision must be a full lowercase 40- or 64-digit hex digest.", nameof(value));
    }

    internal static void Text(string value, int maximum, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum ||
            value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw new ArgumentException($"Text must be nonempty and at most {maximum} characters, without control characters.", parameter);
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                i++;
            else if (char.IsSurrogate(value[i]))
                throw new ArgumentException("Text must contain valid Unicode.", parameter);
        }
    }

    internal static void Scope(string? relativePath, string? symbol)
    {
        if (relativePath is not null)
        {
            Text(relativePath, 512, nameof(relativePath));
            if (relativePath.Any(c => c is '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ||
                char.IsControl(c)) ||
                relativePath.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment) ||
                    segment is "." or ".." || segment != segment.Trim() || segment.EndsWith('.')))
                throw new ArgumentException("Use an exact slash-separated relative path without traversal, aliases, or wildcards.", nameof(relativePath));
        }
        if (symbol is not null)
        {
            if (relativePath is null)
                throw new ArgumentException("A symbol requires a relative file path.", nameof(symbol));
            Text(symbol, 256, nameof(symbol));
            if (symbol != symbol.Trim() || symbol.Any(char.IsControl))
                throw new ArgumentException("A symbol cannot have surrounding whitespace or control characters.", nameof(symbol));
        }
    }

    internal static void Kind(CodeObservationKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
    }
}
