using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace archcartography.src
{
    class Wand : Item
    {

        public List<Vec2i> getChunksForWandMap(ItemSlot slot)
        {
            //side agnostic. though server holds a true state and clients hold a on need to know basis?
            return getChunksForWandMap(getWandMapUUID(api,slot));
        }

        public List<Vec2i> getChunksForWandMap(int uuid)
        {
            return api.ModLoader.GetModSystem<ArchCartography>().GetChunksOnDB(uuid);
        }

        public static List<Vec2i> getChunksForWandMap(int uuid, ICoreClientAPI capi)
        {
            return capi.ModLoader.GetModSystem<ArchCartography>().GetChunksOnDB(uuid);
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
        }

        public override void OnHeldDropped(IWorldAccessor world, IPlayer byPlayer, ItemSlot slot, int quantity, ref EnumHandling handling)
        {
            base.OnHeldDropped(world, byPlayer, slot, quantity, ref handling);
        }

        public bool addChunks(int uuid, Vec2i chunkToAdd)
        {
            //clients are not able to add chunks. this should really throw a warning
            if (api is ICoreClientAPI)
            {
                return false;
            }
            ArchCartography ArchCartography = api.ModLoader.GetModSystem<ArchCartography>();
            if (!ArchCartography.WandHasChunksOnDB(uuid, chunkToAdd))
            {
                ArchCartography.SaveChunksToDB(uuid, chunkToAdd);
                return true;
            }
            return false;
        }

        public int addChunks(int uuid, List<Vec2i> chunksToAdd)
        {
            //clients are not able to add chunks. this should really throw a warning
            if (api is ICoreClientAPI)
            {
                return -1;
            }
            int countAdded = 0;
            foreach (Vec2i chunk in chunksToAdd)
            {
                if (addChunks(uuid,chunk))
                    countAdded++;
            }
            return countAdded;
        }

        public bool removeChunks(int uuid,Vec2i chunkToRemove)
        {
            //clients are not able to remove chunks. this should really throw a warning
            if (api is ICoreClientAPI)
            {
                return false;
            }
            List<Vec2i> chunks = getChunksForWandMap(uuid);
            ArchCartography ArchCartography = api.ModLoader.GetModSystem<ArchCartography>();
            if (chunks.Contains(chunkToRemove))
            {
                return ArchCartography.RemoveChunkOnWandDB(uuid, chunkToRemove);
            }
            return false;
        }

        public int removeChunks(int uuid,List<Vec2i> chunksToRemove)
        {
            //clients are not able to remove chunks. this should really throw a warning
            if (api is ICoreClientAPI)
            {
                return -1;
            }
            int countRemoved = 0;
            foreach (Vec2i chunk in chunksToRemove)
            {
                if (removeChunks(uuid,chunk))
                    countRemoved++;
            }
            return countRemoved;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            
            if (inSlot.Itemstack.Item is Wand)
                dsc.Append("\nMAP:UUID:[" + getWandMapUUID(api, inSlot).ToString() + "]\n");
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        }

        public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
        {
           
            ArchCartography ArchCartography = api.ModLoader.GetModSystem<ArchCartography>();
            //api.Logger.Event("<cartography> LOADED WAND MAP ITEM UNIQUELY [" + this.uuid + "] AND CODE : [" + this.Code.ToString() + "]");

            if (((EntityPlayer)byEntity).LeftHandItemSlot.Equals(slot) || ((EntityPlayer)byEntity).RightHandItemSlot.Equals(slot))
            {
                if (isWandMapInitialised(slot))
                {
                    BlockPos pos = byEntity.Pos.AsBlockPos;
                    int _chunkSize = api.World.BlockAccessor.ChunkSize;
                    Vec2i chunkPos = new Vec2i().Set((int)(pos.X / _chunkSize), (int)(pos.Z / _chunkSize));
                    int uuid = getWandMapUUID(api, slot);
                    if(api is ICoreServerAPI)
                    {
                        if(uuid == -1)
                        {
                            if (api is ICoreClientAPI)
                                throw new System.Exception("the fuck?");
                            uuid = ArchCartography.GetNextUUID((ICoreServerAPI)api);
                            slot.Itemstack.Attributes.SetInt("UUID", uuid);
                            slot.MarkDirty();
                            base.OnHeldIdle(slot, byEntity);
                            api.World.Logger.Event("<cartography> <s> try assign new UUID for map item [" + uuid.ToString() +  "]");
                            return;
                        } else
                        {
                            //api.World.Logger.Event("<cartography> <s> got UUID for map item [" + uuid.ToString() + "]");
                        }
                        bool anyChange = false;
                        UpdateChunksForMap.SyncMode mode;
                        if (((EntityPlayer)byEntity).OnGround)
                        {
                            anyChange = this.addChunks(uuid, chunkPos);
                            mode = UpdateChunksForMap.SyncMode.ADD;
                        }
                        else
                        {
                            anyChange = this.removeChunks(uuid, chunkPos);
                            mode = UpdateChunksForMap.SyncMode.REMOVE;
                        }
                        if (anyChange)
                        {
                            api.World.Logger.Event("<cartography> <s> map has new chunk(s) associated. is Dirty. [" + uuid.ToString() + "]");
                            slot.MarkDirty();
                            //sync ?!
                            //((ICoreServerAPI)api).PlayerData.GetPlayerDataByUid(((EntityPlayer)byEntity).PlayerUID).
                            IServerPlayer player = ((ICoreServerAPI)api).Server.Players.First((IServerPlayer pl) => { return pl.PlayerUID == ((EntityPlayer)byEntity).PlayerUID; });
                            ArchCartography.serverChannel.SendPacket<UpdateChunksForMap>(
                                new UpdateChunksForMap { mapUUID = uuid, chunks = new Vec2i[] { chunkPos }, mode = mode }, player); //, 
                        }
                    }
                    ArchCartography.allRevealedChunks = this.getChunksForWandMap(uuid);
                    if(api is ICoreClientAPI && ArchCartography.isLocalCopyDirty)
                        ArchCartography.updateGui();
                }
            }
            base.OnHeldIdle(slot, byEntity);
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.Handled;
        }

        public static bool isWandMapInitialised(ItemSlot slot)
        {
            return isWandMapInitialised(slot.Itemstack);
        }

        public static bool isWandMapInitialised(ItemStack stack)
        {
            int? uuid = stack.Attributes.TryGetInt("UUID");
            return (uuid != null && uuid > -1);
        }

        public static int getWandMapUUID(ICoreAPI api, ItemSlot slot)
        {
            return getWandMapUUID(api, slot.Itemstack);
        }

        public static int getWandMapUUID(ICoreAPI api, ItemStack stack)
        {
            //TODO : run it server or client sided.
            ArchCartography ArchCartography = api.ModLoader.GetModSystem<ArchCartography>();
            int? uuid = stack.Attributes.TryGetInt("UUID");
            if (uuid == null || (uuid != null && uuid == -1))
            {
                //when retrieved by initialisation of a Map item on server side, will trigger a new UUID and sync to this client.
                //client assumed no id until sync (centralised server DB maanged UUID creation)
                return -1;       
            }
            return stack.Attributes.GetInt("UUID");
        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            return true;
        }
    }
}
