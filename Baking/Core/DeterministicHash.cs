using System.Security.Cryptography;
using System.Text;

namespace WorldBuilder.Baking.Core
{
    public static class DeterministicHash
    {
        public static string Sha256(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                StringBuilder builder = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
                return builder.ToString();
            }
        }

        public static int StableInt32(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                string text = value ?? string.Empty;
                for (int i = 0; i < text.Length; i++) hash = (hash ^ text[i]) * 16777619;
                return (int)hash;
            }
        }
    }
}
