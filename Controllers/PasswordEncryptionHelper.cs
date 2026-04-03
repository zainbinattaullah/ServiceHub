using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class PasswordEncryptionHelper
{
    private static readonly string EncryptionKey = "MySuperSecureKey@123"; 
    private static readonly byte[] SaltBytes = Encoding.ASCII.GetBytes("SaltHere123");
    private const int Iterations = 100_000; // increase iteration count for PBKDF2

    public static string Encrypt(string plainText)
    {
        byte[] clearBytes = Encoding.UTF8.GetBytes(plainText);
        using (Aes aes = Aes.Create())
        {
            var pdb = new Rfc2898DeriveBytes(EncryptionKey, SaltBytes, Iterations, HashAlgorithmName.SHA256);
            aes.Key = pdb.GetBytes(32);
            aes.IV = pdb.GetBytes(16);

            using (var ms = new MemoryStream())
            {
                using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(clearBytes, 0, clearBytes.Length);
                    cs.Close();
                }
                return Convert.ToBase64String(ms.ToArray());
            }
        }
    }

    public static string Decrypt(string encryptedText)
    {
        byte[] cipherBytes = Convert.FromBase64String(encryptedText);
        using (Aes aes = Aes.Create())
        {
            var pdb = new Rfc2898DeriveBytes(EncryptionKey, SaltBytes, Iterations, HashAlgorithmName.SHA256);
            aes.Key = pdb.GetBytes(32);
            aes.IV = pdb.GetBytes(16);

            using (var ms = new MemoryStream())
            {
                using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(cipherBytes, 0, cipherBytes.Length);
                    cs.Close();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }
}
