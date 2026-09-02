using System;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace AO.Client.Backends.AORebirth
{
    // Funcom/live-compatible seeded Diffie-Hellman + chained TEA login response.
    // Project Rubi-Ka uses the same handshake with a different public seed.
    internal static class AOLegacyLoginKey
    {
        private const int Generator = 5;

        public static string Create(string username, string password, byte[] salt,
            string primeHex, string publicSeedHex)
        {
            if (salt == null || salt.Length == 0)
                throw new ArgumentException("A server salt is required.", nameof(salt));
            if (string.IsNullOrWhiteSpace(primeHex) || string.IsNullOrWhiteSpace(publicSeedHex))
                throw new ArgumentException("Legacy authentication seeds are required.");

            byte[] exponentBytes = new byte[16];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                random.GetBytes(exponentBytes);
            string exponentHex = ToHex(exponentBytes);

            BigInteger prime = ParsePositiveHex(primeHex);
            BigInteger publicSeed = ParsePositiveHex(publicSeedHex);
            BigInteger exponent = ParsePositiveHex(exponentHex);
            // BigInteger's hexadecimal formatter may add a leading zero nibble to
            // keep a positive value from looking signed. AO's legacy client strips
            // that nibble before selecting the first 16 bytes as the TEA key.
            string shared = ToUnsignedHex(BigInteger.ModPow(publicSeed, exponent, prime));
            string clientPublic = ToUnsignedHex(
                BigInteger.ModPow(new BigInteger(Generator), exponent, prime));
            string plaintext = username + "|" + Encoding.ASCII.GetString(salt) + "|" + password;
            return (clientPublic + "-" + Encrypt(plaintext, shared)).ToLowerInvariant();
        }

        private static string Encrypt(string value, string keyHex)
        {
            if (keyHex.Length < 32)
                throw new InvalidOperationException("The negotiated login key is too short.");

            byte[] key = FromHex(keyHex.Substring(0, 32));
            byte[] randomPrefix = new byte[8];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                random.GetBytes(randomPrefix);

            byte[] valueBytes = Encoding.ASCII.GetBytes(value);
            int length = 8 - ((valueBytes.Length - 1 + 12) % 8) + valueBytes.Length - 1 + 12;
            byte[] buffer = new byte[length];
            for (int i = 0; i < buffer.Length; i++) buffer[i] = 32;
            Buffer.BlockCopy(randomPrefix, 0, buffer, 0, randomPrefix.Length);
            byte[] networkLength = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(valueBytes.Length));
            Buffer.BlockCopy(networkLength, 0, buffer, 8, networkLength.Length);
            Buffer.BlockCopy(valueBytes, 0, buffer, 12, valueBytes.Length);

            uint previous0 = 0, previous1 = 0;
            for (int offset = 0; offset < buffer.Length; offset += 8)
            {
                uint left = BitConverter.ToUInt32(buffer, offset) ^ previous0;
                uint right = BitConverter.ToUInt32(buffer, offset + 4) ^ previous1;
                EncryptTea(ref left, ref right, key);
                Buffer.BlockCopy(BitConverter.GetBytes(left), 0, buffer, offset, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(right), 0, buffer, offset + 4, 4);
                previous0 = left;
                previous1 = right;
            }
            return ToHex(buffer);
        }

        private static void EncryptTea(ref uint left, ref uint right, byte[] key)
        {
            uint k0 = BitConverter.ToUInt32(key, 0), k1 = BitConverter.ToUInt32(key, 4);
            uint k2 = BitConverter.ToUInt32(key, 8), k3 = BitConverter.ToUInt32(key, 12);
            uint sum = 0;
            for (int round = 0; round < 32; round++)
            {
                sum += 0x9E3779B9;
                left += ((right << 4) + k0) ^ (right + sum) ^ ((right >> 5) + k1);
                right += ((left << 4) + k2) ^ (left + sum) ^ ((left >> 5) + k3);
            }
        }

        private static BigInteger ParsePositiveHex(string value)
        {
            return BigInteger.Parse("00" + value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        private static string ToUnsignedHex(BigInteger value)
        {
            string result = value.ToString("X", CultureInfo.InvariantCulture).TrimStart('0');
            return result.Length == 0 ? "0" : result;
        }

        private static byte[] FromHex(string value)
        {
            byte[] result = new byte[value.Length / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = byte.Parse(value.Substring(i * 2, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture);
            return result;
        }

        private static string ToHex(byte[] value)
        {
            var result = new StringBuilder(value.Length * 2);
            for (int i = 0; i < value.Length; i++)
                result.Append(value[i].ToString("x2", CultureInfo.InvariantCulture));
            return result.ToString();
        }
    }
}
