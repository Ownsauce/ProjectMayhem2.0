using System;
using System.Collections.Generic;

namespace AO.Core.Items
{
    public static class ItemLoader
    {
        public static List<ItemDefinition> LoadFromJson(string path)
        {
            throw new NotSupportedException(
                "JSON loading is handled by the client layer (AO.Client / AO.Tools).");
        }
    }
}
