using System;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text;

namespace AO.Client.Backends.AORebirth
{
    internal static class AORebirthLoginKey
    {
        // Protocol constants verified against AORebirth's ProductionLoginAcceptance tool.
        private const string ClientPublicKey =
            "8f2d7c34a0b9e8d6c5f4a3928170615049382716f5e4d3c2b1a09876543210fedcba98765432100123456789abcdef";

        private const string ServerPrivateKey =
            "7ad852c6494f664e8df21446285ecd6f400cf20e1d872ee96136d7744887424b";

        private const string Prime =
            "eca2e8c85d863dcdc26a429a71a9815ad052f6139669dd659f98ae159d313d13c6bf2838e10a69b6478b64a24bd054ba8248e8fa778703b418408249440b2c1edd28853e240d8a7e49540b76d120d3b1ad2878b1b99490eb4a2a5e84caa8a91cecbdb1aa7c816e8be343246f80c637abc653b893fd91686cf8d32d6cfe5f2a6f";

        public static string Create(string username, string password, byte[] serverSalt)
        {
            if (serverSalt == null || serverSalt.Length != 32)
                throw new ArgumentException("The server salt must contain 32 bytes.", nameof(serverSalt));

            string teaKey = ComputeTeaKey();
            string plaintext = CreatePlaintext(username, password, serverSalt);
            return ClientPublicKey + "-" + EncryptTea(plaintext, teaKey);
        }

        private static string ComputeTeaKey()
        {
            BigInteger publicKey = ParsePositiveHex(ClientPublicKey);
            BigInteger privateKey = ParsePositiveHex(ServerPrivateKey);
            BigInteger prime = ParsePositiveHex(Prime);
            string key = BigInteger.ModPow(publicKey, privateKey, prime).ToString("x");
            return key.Length < 32 ? key.PadLeft(32, '0') : key.Substring(0, 32);
        }

        private static BigInteger ParsePositiveHex(string hex)
        {
            if (hex.Length % 2 != 0)
                hex = "0" + hex;

            byte[] bytes = new byte[(hex.Length / 2) + 1];
            for (int source = 0, target = bytes.Length - 2; source < hex.Length; source += 2, target--)
            {
                bytes[target] = byte.Parse(
                    hex.Substring(source, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture);
            }

            return new BigInteger(bytes);
        }

        private static string CreatePlaintext(string username, string password, byte[] salt)
        {
            int dataLength = username.Length + password.Length + 34;
            var builder = new StringBuilder();
            builder.Append("AOLOGIN!");
            AppendBigEndianInt32(builder, dataLength);
            builder.Append(username);
            builder.Append('|');
            for (int i = 0; i < salt.Length; i++)
                builder.Append((char)salt[i]);
            builder.Append('|');
            builder.Append(password);
            while (builder.Length % 8 != 0)
                builder.Append('\0');
            return builder.ToString();
        }

        private static void AppendBigEndianInt32(StringBuilder builder, int value)
        {
            builder.Append((char)((value >> 24) & 0xFF));
            builder.Append((char)((value >> 16) & 0xFF));
            builder.Append((char)((value >> 8) & 0xFF));
            builder.Append((char)(value & 0xFF));
        }

        private static string EncryptTea(string plaintext, string key)
        {
            uint[] keyWords =
            {
                ParseNetworkUInt32(key.Substring(0, 8)),
                ParseNetworkUInt32(key.Substring(8, 8)),
                ParseNetworkUInt32(key.Substring(16, 8)),
                ParseNetworkUInt32(key.Substring(24, 8))
            };
            uint previous0 = 0;
            uint previous1 = 0;
            var encrypted = new StringBuilder();

            for (int index = 0; index < plaintext.Length; index += 8)
            {
                uint[] block =
                {
                    ReadLittleEndianUInt32(plaintext, index) ^ previous0,
                    ReadLittleEndianUInt32(plaintext, index + 4) ^ previous1
                };
                EncryptTeaBlock(block, keyWords);
                encrypted.Append(ToNetworkHex(block[0]));
                encrypted.Append(ToNetworkHex(block[1]));
                previous0 = block[0];
                previous1 = block[1];
            }

            return encrypted.ToString();
        }

        private static uint ReadLittleEndianUInt32(string value, int offset)
        {
            return (uint)value[offset]
                   | ((uint)value[offset + 1] << 8)
                   | ((uint)value[offset + 2] << 16)
                   | ((uint)value[offset + 3] << 24);
        }

        private static uint ParseNetworkUInt32(string value)
        {
            uint parsed = uint.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return unchecked((uint)IPAddress.NetworkToHostOrder(unchecked((int)parsed)));
        }

        private static string ToNetworkHex(uint value)
        {
            int networkValue = IPAddress.HostToNetworkOrder(unchecked((int)value));
            return unchecked((uint)networkValue).ToString("x8", CultureInfo.InvariantCulture);
        }

        private static void EncryptTeaBlock(uint[] data, uint[] key)
        {
            uint sum = 0;
            const uint Delta = 0x9e3779b9;
            for (int round = 0; round < 32; round++)
            {
                sum += Delta;
                data[0] += ((data[1] << 4) + key[0]) ^ (data[1] + sum) ^ ((data[1] >> 5) + key[1]);
                data[1] += ((data[0] << 4) + key[2]) ^ (data[0] + sum) ^ ((data[0] >> 5) + key[3]);
            }
        }
    }
}
