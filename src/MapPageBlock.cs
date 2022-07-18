using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.API.Util;

namespace archcartography.src
{
    class MapPageBlock : Block
    {
        ICoreAPI Api;
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            this.Api = api;

            if (api.Side != EnumAppSide.Client) return;

            
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            BlockEntity Entity = world.BlockAccessor.GetBlockEntity(blockSel.Position);
            Api.World.Logger.Event("<api:OnBlockInteractStart> MapPageBlock UUID [" + ((MapPageBlockEntity)Entity).UUID.ToString() + "]");
            //if (Entity is BlockEntityBooks)
            //{
            //BlockEntityBooks BEBooks = (BlockEntityBooks)Entity;
            //BEBooks.OnRightClick(byPlayer, false);
            //return true;
            //}
            return true;
        }

        public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack)
        {
            base.OnBlockPlaced(world, blockPos, byItemStack);

            if (world.Api is ICoreServerAPI)
            {
                BlockEntity be = world.BlockAccessor.GetBlockEntity(blockPos) as MapPageBlockEntity;
                if (be is MapPageBlockEntity)
                {
                    MapPageBlockEntity MPBE;
                    MPBE = (MapPageBlockEntity)be;
                    MPBE.UUID = byItemStack.Attributes.GetInt("UUID", 1);
                    world.BlockAccessor.MarkBlockDirty(blockPos);
                    world.BlockAccessor.MarkBlockEntityDirty(blockPos);
                }
            }
        }

    }
}
