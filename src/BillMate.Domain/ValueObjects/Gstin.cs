using System.Text.RegularExpressions;
using BillMate.Domain.Exceptions;

namespace BillMate.Domain.ValueObjects;

/// <summary>
/// Validated 15-character Indian GSTIN value object with state code extraction.
/// </summary>
public sealed partial record Gstin
{
    // Format: 2 digits state code + 10 char PAN (5 alpha, 4 digits, 1 alpha) + 1 entity no + 'Z' + 1 checksum char
    [GeneratedRegex(@"^(0[1-9]|[1-2][0-9]|3[0-8]|97)[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$", RegexOptions.Compiled)]
    private static partial Regex GstinPattern();

    public string Value { get; }
    public string StateCode => Value[..2];
    public string Pan => Value[2..12];

    private Gstin(string value) => Value = value;

    public static Gstin Create(string rawGstin)
    {
        if (string.IsNullOrWhiteSpace(rawGstin))
            throw new DomainException("VAL_002", "GSTIN cannot be empty.");

        var normalized = rawGstin.Trim().ToUpperInvariant();
        if (!GstinPattern().IsMatch(normalized))
            throw new DomainException("VAL_003", $"Invalid Indian GSTIN format: '{rawGstin}'.");

        return new Gstin(normalized);
    }

    public static Gstin? CreateOptional(string? rawGstin) =>
        string.IsNullOrWhiteSpace(rawGstin) ? null : Create(rawGstin);

    public override string ToString() => Value;
}
