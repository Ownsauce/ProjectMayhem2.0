using System;

namespace AO.Core.Items
{
    public class ItemInstance
    {
        public ItemDefinition Definition { get; }
        public int Quantity { get; set; }
        public long InstanceId { get; }
        public int ContainerCapacity { get; }
        public bool IsContainer => ContainerCapacity > 0;

        public ItemInstance(
            ItemDefinition definition,
            int quantity = 1,
            long instanceId = 0,
            int containerCapacity = 0)
        {
            Definition = definition;
            Quantity = quantity;
            InstanceId = instanceId;
            ContainerCapacity = containerCapacity;
        }
    }
}
