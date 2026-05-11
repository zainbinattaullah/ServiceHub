using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ServiceHub.Areas.HR.Controllers
{
    [Area("HR")]
    [Authorize]
    public class AdminDecryptController : Controller
    {
        private static readonly string EncryptionKey = "MySuperSecureKey@123";
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Decrypt(string encryptedText)
        {
            if (string.IsNullOrWhiteSpace(encryptedText))
            {
                ViewBag.Error = "Please enter an encrypted password.";
                return View("Index");
            }
            try
            {
                string decrypted = DecryptPassword(encryptedText);
                ViewBag.Decrypted = decrypted;
                ViewBag.Original = encryptedText;
            }
            catch
            {
                ViewBag.Error = "Invalid or corrupt encrypted string.";
            }
            return View("Index");
        }

        private string DecryptPassword(string encryptedText)
        {
            byte[] cipherBytes = Convert.FromBase64String(encryptedText);
            using (Aes aes = Aes.Create())
            {
                var salt = Encoding.ASCII.GetBytes("SaltHere123");
                // Use a modern hash algorithm and higher iteration count to avoid SYSLIB0041
                using (var pdb = new Rfc2898DeriveBytes(EncryptionKey, salt, 100_000, HashAlgorithmName.SHA256))
                {
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
    }
}
