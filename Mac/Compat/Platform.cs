using System.Security.Cryptography;

namespace System.Security.Cryptography
{
    public enum DataProtectionScope { CurrentUser, LocalMachine }

    /// <summary>
    /// Stands in for Windows DPAPI on macOS: AES-GCM with a random key kept in the user's own
    /// Application Support folder, readable only by that user.
    /// </summary>
    public static class ProtectedData
    {
        static string KeyPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetroRadio", ".key");

        static byte[] Key()
        {
            try
            {
                if (File.Exists(KeyPath))
                {
                    var k = File.ReadAllBytes(KeyPath);
                    if (k.Length == 32) return k;
                }
            }
            catch (IOException) { }
            var key = RandomNumberGenerator.GetBytes(32);
            Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
            File.WriteAllBytes(KeyPath, key);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return key;
        }

        public static byte[] Protect(byte[] userData, byte[]? optionalEntropy, DataProtectionScope scope)
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var cipher = new byte[userData.Length];
            using var aes = new AesGcm(Key(), 16);
            aes.Encrypt(nonce, userData, cipher, tag, optionalEntropy);
            return [.. nonce, .. tag, .. cipher];
        }

        public static byte[] Unprotect(byte[] encryptedData, byte[]? optionalEntropy, DataProtectionScope scope)
        {
            if (encryptedData.Length < 28) throw new CryptographicException("Bad data.");
            var plain = new byte[encryptedData.Length - 28];
            using var aes = new AesGcm(Key(), 16);
            aes.Decrypt(encryptedData.AsSpan(0, 12), encryptedData.AsSpan(28), encryptedData.AsSpan(12, 16), plain, optionalEntropy);
            return plain;
        }
    }
}

namespace RetroRadio
{
    /// <summary>On macOS windows keep the system's own title bar; the Windows 7 glass frame is Windows-only.</summary>
    static class ClassicFrame
    {
        public static void Apply(System.Windows.Forms.Form content) { }
    }
}
