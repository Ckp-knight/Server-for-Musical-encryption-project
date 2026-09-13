namespace MusicalEncryptionServer.Services;

/// <summary>
/// The musical-encryption algorithm. The rest of the server only talks to this
/// interface, so you can build/replace the real algorithm without touching the
/// controller or the database layer.
/// </summary>
public interface IMusicalCipher
{
    string AlgorithmName { get; }

    string Encrypt(string plainText, string key);

    string Decrypt(string cipherText, string key);
}
