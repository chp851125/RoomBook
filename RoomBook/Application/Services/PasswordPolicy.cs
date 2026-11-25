using System.Security.Cryptography;

namespace RoomBook.Application.Services;

public static class PasswordPolicy
{
    /// <summary>密碼原則：長度不得少於設定值，且須同時包含英文字母與數字。</summary>
    public static string? Validate(string? password, int minLength)
    {
        if (string.IsNullOrEmpty(password) || password.Length < minLength) return $"密碼至少需要 {minLength} 個字元。";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)) return "密碼需同時包含英文字母與數字。";
        return null;
    }

    /// <summary>產生臨時密碼（避開容易混淆的字元）。</summary>
    public static string Generate(int length = 12)
    {
        const string letters = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "23456789";
        const string symbols = "#$%&*?";
        var all = letters + digits + symbols;
        var chars = new List<char>
        {
            letters[RandomNumberGenerator.GetInt32(letters.Length)],
            digits[RandomNumberGenerator.GetInt32(digits.Length)],
            symbols[RandomNumberGenerator.GetInt32(symbols.Length)]
        };
        while (chars.Count < Math.Max(length, 8)) chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);
        return new string(chars.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToArray());
    }
}
