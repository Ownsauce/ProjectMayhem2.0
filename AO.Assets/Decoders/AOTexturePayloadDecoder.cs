using System;
using System.IO;

namespace AO.Assets.Decoders
{
    /// <summary>Reads the original encoded image; no PNG export or image conversion is needed.</summary>
    public static class AOTexturePayloadDecoder
    {
        public static byte[] Decode(byte[] record, int resourceType, int resourceId)
        {
            if (resourceType != 1010004 && resourceType != 1010009)
                throw new InvalidDataException("Unsupported indoor texture resource type");
            if (record == null || record.Length < 16)
                throw new InvalidDataException("Truncated AO image record");
            using (var reader = new BinaryReader(new MemoryStream(record, false)))
            {
                if (reader.ReadInt32() != resourceType || reader.ReadInt32() != resourceId)
                    throw new InvalidDataException("AO image record identity mismatch");
                reader.ReadInt32(); // resource envelope version; the image follows directly
                bool jpeg = record[12] == 0xff && record[13] == 0xd8;
                bool png = record.Length >= 20 && record[12] == 137 && record[13] == 80
                    && record[14] == 78 && record[15] == 71 && record[16] == 13
                    && record[17] == 10 && record[18] == 26 && record[19] == 10;
                if (!jpeg && !png) throw new InvalidDataException("Unsupported AO image encoding");
                return reader.ReadBytes(record.Length - 12);
            }
        }
    }
}
