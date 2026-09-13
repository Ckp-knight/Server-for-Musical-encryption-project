using System.Text;

namespace MusicalEncryptionServer.Services;

/// <summary>
/// TEMPORARY implementation so the server runs end-to-end (encrypt -> store -> decrypt).
/// It is a simple reversible XOR + Base64 — NOT secure and NOT musical.
/// TODO: replace with the real musical-encryption algorithm. Keep the same
/// interface and nothing else in the project needs to change.
/// </summary>
public class PlaceholderMusicalCipher : IMusicalCipher
{
    public string AlgorithmName => "placeholder-xor-v1";

    public string Encrypt(string plainText, string key)
        => Convert.ToBase64String(Xor(Encoding.UTF8.GetBytes(plainText), key));

    public string Decrypt(string cipherText, string key)
        => Encoding.UTF8.GetString(Xor(Convert.FromBase64String(cipherText), key));

    private static byte[] Xor(byte[] data, string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key is required.", nameof(key));

        var keyBytes = Encoding.UTF8.GetBytes(key);
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] ^ keyBytes[i % keyBytes.Length]);
        return result;
    }
}
