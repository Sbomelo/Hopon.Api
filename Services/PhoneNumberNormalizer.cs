using System.Text;

namespace Hopon.Api.Services;

public static class PhoneNumberNormalizer
{
    public static bool TryNormalizeSouthAfrican(string? input, out string e164)
    {
        e164 = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var compact = new StringBuilder();
        foreach (var character in input)
        {
            if (character is >= '0' and <= '9' or '+')
                compact.Append(character);
            else if (character is not (' ' or '(' or ')' or '-'))
                return false;
        }

        var number = compact.ToString();
        string nationalNumber;
        if (number.StartsWith("+27", StringComparison.Ordinal) && number.Length == 12)
            nationalNumber = number[3..];
        else if (number.StartsWith("0027", StringComparison.Ordinal) && number.Length == 13)
            nationalNumber = number[4..];
        else if (number.StartsWith("27", StringComparison.Ordinal) && number.Length == 11)
            nationalNumber = number[2..];
        else if (number.StartsWith('0') && number.Length == 10)
            nationalNumber = number[1..];
        else
            return false;

        if (nationalNumber[0] is not ('6' or '7' or '8'))
            return false;

        foreach (var digit in nationalNumber)
        {
            if (digit is < '0' or > '9')
                return false;
        }

        e164 = "+27" + nationalNumber;
        return true;
    }
}
