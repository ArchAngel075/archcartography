using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using HarmonyLib;
using ProtoBuf;
using System.Reflection;

namespace archcartography.src
{

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class UpdateChunksForMap
    {
        public enum SyncMode
        {
            ADD, REMOVE, SYNC
        }
        public SyncMode mode;
        public Vec2i[] chunks;
        public int mapUUID;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class SetMapUUIDForItemSlotOfPlayer
    {
        public int slotOfPlayer;
        public int wandUUID;
    }

    class ArchCartography : ModSystem
    {
        public List<Vec2i> allTouchedChunks = new List<Vec2i>();
        public List<Vec2i> allRevealedChunks = new List<Vec2i>();
        public bool hasMap = false;
        public bool hadMapLast = false;
        public Vec2i chunkPosLast = null;
        public int wandUUIDLast = -1;
        public ICoreClientAPI capi;
        public ICoreServerAPI sapi;
        private ICoreAPI api;
        private long tryOverrideOnTickID;
        private bool hasOverridedHUD = false;
        private bool hasOverridedDial = false;
        WandMapDB wandMapDBClient;
        WandMapDB wandMapDBServer;
        GuiDialog dialog;
        public IServerNetworkChannel serverChannel;
        public IClientNetworkChannel clientChannel;
        public bool isLocalCopyDirty = false;

        public WandMapDB GetWandMapDB(ICoreAPI api)
        {
            if (api is ICoreServerAPI)
                return wandMapDBServer;
            if (api is ICoreClientAPI)
                return wandMapDBClient;
            return null;
        }

        public WandMapDB initWandMapDB()
        {
            string errorMessage = null;
            string mapdbfilepath = getMapDbFilePath();
            if (api is ICoreServerAPI)
            {
                wandMapDBServer = new WandMapDB(api.World.Logger, api);
                wandMapDBServer.OpenOrCreate(mapdbfilepath, ref errorMessage, true, true, false);
                if (errorMessage != null)
                {
                    throw new Exception(string.Format("Cannot open {0}, possibly corrupted. Please fix manually or delete this file to continue playing", mapdbfilepath));
                }
                return wandMapDBServer;
            }
            if (api is ICoreClientAPI)
            {
                wandMapDBClient = new WandMapDB(api.World.Logger, api);
                wandMapDBClient.OpenOrCreate(mapdbfilepath, ref errorMessage, true, true, false);
                if (errorMessage != null)
                {
                    throw new Exception(string.Format("Cannot open {0}, possibly corrupted. Please fix manually or delete this file to continue playing", mapdbfilepath));
                }
                return wandMapDBClient;
            }
            return null;
        }

        public void SaveChunksToDB(int wandID, Vec2i chunk)
        {
            EntityPlayer p;
            GetWandMapDB(api).AddChunkToWand(wandID, chunk);
        }

        public bool RemoveChunkOnWandDB(int wandID, Vec2i chunk)
        {
            return GetWandMapDB(api).DeleteChunkFromWand(wandID, chunk);
        } 

        public List<Vec2i> GetChunksOnDB(int wandID)
        {
            return GetWandMapDB(api).GetChunksOf(wandID);
        }

        public bool WandHasChunksOnDB(int wandID, Vec2i chunk)
        {
            return GetWandMapDB(api).HasWandChunk(wandID,chunk);
        }

        public int GetNextUUID(ICoreServerAPI api)
        {
            return GetWandMapDB((ICoreAPI)api).GetNextUUID();
        }

        public override bool ShouldLoad(EnumAppSide side)
           => side == EnumAppSide.Client || side == EnumAppSide.Server;


        public override void Start(ICoreAPI api)
        {
            this.api = api;
            var harmony = new Harmony("com.archangel075.vintagestory.archcartography");
            base.Start(api);
            initWandMapDB();

            
            //api.RegisterBlockClass("trampoline", typeof(TrampolineBlock));
            api.World.Logger.Event("started 'Arch Cartography' mod");
            api.RegisterItemClass("Wand", typeof(Wand));
            api.RegisterBlockClass("MapPageBlock", typeof(MapPageBlock));
            api.RegisterBlockEntityClass("MapPageBlockEntity", typeof(MapPageBlockEntity));
        }



        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            this.sapi = api;
            sapi.World.Logger.Event("<server> linked 'Arch Cartography' mod sapi");
            sapi.Event.BeforeActiveSlotChanged += Event_BeforeActiveSlotChangedServer;
            sapi.Event.DidPlaceBlock += Event_DidPlaceBlock; ;
            serverChannel = sapi.Network.RegisterChannel("cartography")
                .RegisterMessageType(typeof(UpdateChunksForMap))
                .RegisterMessageType(typeof(SetMapUUIDForItemSlotOfPlayer));
            //Event_DidPlaceBlock(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel, ItemStack withItemStack)
        }

        private EnumHandling Event_BeforeActiveSlotChangedServer(IServerPlayer serverPlayer, ActiveSlotChangeEventArgs eventArgs)
        {
            if(api is ICoreClientAPI)
            {
                api.World.Logger.Event("client doesnt handle Event_BeforeActiveSlotChangedServer");
                return EnumHandling.PassThrough;
            } else
            {
                api.World.Logger.Event("Server handling Event_BeforeActiveSlotChangedServer ->");
            }
            ItemStack stack = serverPlayer.InventoryManager.GetHotbarItemstack(eventArgs.ToSlot);
            if(stack != null && stack.Item is Wand)
            {
                api.World.Logger.Event("item is map");
                int uuid = Wand.getWandMapUUID(sapi, stack);
                if (!Wand.isWandMapInitialised(stack))
                {
                    api.World.Logger.Event("map was not init -> do so.");
                    uuid = GetNextUUID((ICoreServerAPI)api);
                    int playerSlot = eventArgs.ToSlot;
                    stack.Attributes.SetInt("UUID",uuid);
                    serverPlayer.InventoryManager.GetHotbarInventory().MarkSlotDirty(playerSlot);
                    //serverChannel
                    //    .SendPacket<SetMapUUIDForItemSlotOfPlayer>(new SetMapUUIDForItemSlotOfPlayer() { slotOfPlayer = playerSlot, wandUUID=uuid }, serverPlayer);
                    //^ handled by dirty? ^
                    serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, "setup new UUID for map [" + uuid.ToString() + "]", EnumChatType.Notification);
                } else
                {
                    api.World.Logger.Event("map is init. send chunks");
                    //send through the wands chunks - the player will need it.
                    List<Vec2i> chunksOnDB = GetChunksOnDB(uuid);
                    serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, "send chunks for map [" + uuid.ToString() + "] size : [" + chunksOnDB.Count.ToString() + "]", EnumChatType.Notification);
                    serverChannel
                        .SendPacket(new UpdateChunksForMap() { mapUUID = uuid,  chunks=chunksOnDB.ToArray(), mode = UpdateChunksForMap.SyncMode.SYNC},serverPlayer);
                }
            }
            api.World.Logger.Event("<- Server handling Event_BeforeActiveSlotChangedServer");
            return EnumHandling.PassThrough;
        }

        private void Event_DidPlaceBlock(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel, ItemStack withItemStack)
        {
            sapi.World.Logger.Event("<server> <cartography> block placed ");
            IMapChunk mapchunk = sapi.World.BlockAccessor.GetMapChunkAtBlockPos(blockSel.Position);
            if (mapchunk == null) return;
            BlockPos pos = blockSel.Position;
            int _chunkSize = sapi.World.BlockAccessor.ChunkSize;
            Vec2i chunkPos = new Vec2i().Set((int)(pos.X / _chunkSize), (int)(pos.Z / _chunkSize));
            
            WorldMapManager WorldMapManager = sapi.ModLoader.GetModSystem<WorldMapManager>();
            ChunkMapLayer cml = (ChunkMapLayer)WorldMapManager.MapLayers.First();
            UniqueQueue<Vec2i> queue = GetChunkMapLayerChunksToGenerate(cml);

            
            sapi.World.Logger.Event("<server> <cartography> count chunks of 2gen ["+ queue.Count().ToString()+ "]");
            if(queue.Count() > 0)
                sapi.World.Logger.Event("<server> <cartography> first chunk of 2gen @ (" + queue.First().X.ToString() + ", " + queue.First().Y.ToString() + ")");
            //WorldMapManager.worldMapDlg.SingleComposer.GetElement("mapElem")

        }

        private bool ToggleGui(KeyCombination comb)
        {
            if (dialog.IsOpened()) dialog.TryClose();
            else dialog.TryOpen();

            return true;
        }

        public string getMapDbFilePath()
        {
            string path = Path.Combine(GamePaths.DataPath, "Maps");
            GamePaths.EnsurePathExists(path);
            string apiside = api.Side == EnumAppSide.Client ? "c" : "s";
            return Path.Combine(path, "wandmaps_" + apiside + "_" + api.World.SavegameIdentifier + ".db");
        }

        public override void Dispose()
        {
            //wandMapDB.Purge(); //urge on dispose for now;
            //GetWandMapDB(api)?.Dispose();
            wandMapDBServer?.Close();
            wandMapDBClient?.Close();
            api.World.Logger.Event("closed and disposed both database instances");
            base.Dispose();
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            this.capi = api;
            capi.World.Logger.Event("linked 'Arch Cartography' mod capi");
            
            capi.Event.PlayerEntitySpawn += Event_PlayerEntitySpawn; ;
            //capi.Event.RegisterGameTickListener
            capi.Event.LevelFinalize += Event_LevelFinalize;
            WorldMapManager manager = capi.ModLoader.GetModSystem<WorldMapManager>();
            if (!(manager is null))
            {
                if (!(manager.worldMapDlg is null))
                    capi.World.Logger.Event("<cartography> map dialog has elements count [" + manager.worldMapDlg.Composers.Count().ToString() + "]");
                //capi.World.Logger.Event("<cartography> get map element");
                //GuiElementMap mapElem = manager.worldMapDlg.Composers.First().Value.GetElement("mapElem") as GuiElementMap;
                //mapElem.viewChanged = onViewChangedClientOverride;
            }
            else
            {
                throw new Exception("unable to get world map manager on client api");
            }

            dialog = new GuiDialogChunksInMap(capi);

            capi.Input.RegisterHotKey("chunksinmapgui", "shows what chunks count are in map item", GlKeys.U, HotkeyType.GUIOrOtherControls);
            capi.Input.SetHotKeyHandler("chunksinmapgui", ToggleGui);
            clientChannel = capi.Network.RegisterChannel("cartography")
                .RegisterMessageType(typeof(UpdateChunksForMap))
                .RegisterMessageType(typeof(SetMapUUIDForItemSlotOfPlayer))
                .SetMessageHandler<UpdateChunksForMap>(OnUpdateChunksForMapClient)
            ;
        }


        private void OnUpdateChunksForMapClient(UpdateChunksForMap msg)
        {
            this.capi.World.Logger.Event("Sync chunks for map [" + msg.mapUUID.ToString() + "]. Mode [" + msg.mode.ToString() + "]");
            this.capi.World.Logger.Event("is map null [" + (msg.chunks == null).ToString() + "]");
            WandMapDB.ChunkSyncResult result = new WandMapDB.ChunkSyncResult() { };
            if (msg.chunks != null)
            {
                this.capi.World.Logger.Event("chunk count in map [" + msg.chunks.Length + "]");
                if (msg.mode == UpdateChunksForMap.SyncMode.SYNC)
                {
                    result  = GetWandMapDB(api).SyncMapChunksToDB(msg.chunks.ToList(), msg.mapUUID);
                    isLocalCopyDirty = true;
                }
                else if(msg.mode == UpdateChunksForMap.SyncMode.ADD)
                {
                    result = new WandMapDB.ChunkSyncResult(){ added = GetWandMapDB(api).AddChunksToWand(msg.mapUUID, msg.chunks), removed = 0 };
                    isLocalCopyDirty = true;
                }
                else if (msg.mode == UpdateChunksForMap.SyncMode.REMOVE)
                {
                    result = new WandMapDB.ChunkSyncResult() { added = 0, removed = GetWandMapDB(api).RemoveChunksFromWand(msg.mapUUID, msg.chunks) };
                    isLocalCopyDirty = true;
                }
            } else
            {
                this.api.World.Logger.Event("unable to sync chunks for map [" + msg.mapUUID.ToString() + "] : The chunks given were null?");
            }
            this.api.World.Logger.Event("Sync chunks for map [" + msg.mapUUID.ToString() + "] Added [" + result.added.ToString() + "] Removed [" + result.removed.ToString() + "]");
        }


        public void updateGui()
        {
            //player and get item held and if wand item and chunks on :
            EntityPlayer player = capi.World.Player.Entity;
            Wand theWand = null;
            bool hasWand = false;
            ItemStack wandItemStack = null;
            ItemSlot wandItemSlot = null;
            if (player != null)
            {

                if (!player.LeftHandItemSlot.Empty && player.LeftHandItemSlot.Itemstack.Item is Wand)
                {
                    hasWand = true;
                    theWand = (Wand)player.LeftHandItemSlot.Itemstack.Item;
                    wandItemStack = player.LeftHandItemSlot.Itemstack;
                    wandItemSlot = player.LeftHandItemSlot;
                }
                else if (!player.RightHandItemSlot.Empty && player.RightHandItemSlot.Itemstack.Item is Wand)
                {
                    hasWand = true;
                    theWand = (Wand)player.RightHandItemSlot.Itemstack.Item;
                    wandItemStack = player.RightHandItemSlot.Itemstack;
                    wandItemSlot = player.RightHandItemSlot;
                }
            }
            if (hasWand)
            {
                ((GuiElementDynamicText)dialog.Composers.First().Value.GetElement("chunkCountText")).SetNewText("The count of chunks on map " + ("[" + wandItemStack.Attributes.TryGetInt("UUID").ToString() + "] [" + theWand.getChunksForWandMap(wandItemSlot).Count().ToString() + "] chunks inside it").ToString());
            } else
            {
                ((GuiElementDynamicText)dialog.Composers.First().Value.GetElement("chunkCountText")).SetNewText("The count of chunks on map [" + ("hold a map wand!").ToString() + "]");
            }
        }

        private void Event_LevelFinalize()
        {
            capi.World.Logger.Event("<cartography> level finalized!");
            WorldMapManager manager = capi.ModLoader.GetModSystem<WorldMapManager>();
            Action<float> action = TryOverrideViewChanged;
            tryOverrideOnTickID = capi.Event.RegisterGameTickListener(action, 20, 1);
        }

        void OnOffOverride(float dt)
        {

        }

        void TryOverrideViewChanged(float f)
        {
            //capi.World.Logger.Event("<cartography> try override on tick!");
            WorldMapManager manager = capi.ModLoader.GetModSystem<WorldMapManager>();
            if (!(manager is null))
            {
                if (!(manager.worldMapDlg is null))
                {
                    GuiElementMap mapElem = manager.worldMapDlg.Composers.First().Value.GetElement("mapElem") as GuiElementMap;
                    
                    if (manager.worldMapDlg.DialogType == EnumDialogType.Dialog && !hasOverridedDial)
                    {
                        mapElem.viewChanged = onViewChangedClientOverrideDebug;
                        hasOverridedDial = true;
                        capi.World.Logger.Event("<cartography> <override> override view changed client event delegate Dial");
                    }

                    if (manager.worldMapDlg.DialogType == EnumDialogType.HUD && !hasOverridedHUD)
                    {
                        mapElem.viewChanged = onViewChangedClientOverrideDebug;
                        hasOverridedHUD = true;
                        capi.World.Logger.Event("<cartography> <override> override view changed client event delegate HUD");
                    }

                    if (hasOverridedHUD && hasOverridedDial)//we dont need to check anymore as both types are overrided
                    {
                        capi.World.Logger.Event("<cartography> <TryOverrideViewChanged> unregister");
                        capi.Event.UnregisterGameTickListener(tryOverrideOnTickID);
                    }
                }
                else
                {
                    capi.World.Logger.Event("<cartography> <TryOverrideViewChanged> map dialog was not ready");
                    if (!manager.IsOpened)
                    {
                        //force show the map.
                        manager.ToggleMap(EnumDialogType.Dialog);
                    }
                }

            }
        }

        private void Event_PlayerEntitySpawn(IClientPlayer byPlayer)
        {
            capi.World.Logger.Event("<cartography> an player spawned!");
            WorldMapManager manager = capi.ModLoader.GetModSystem<WorldMapManager>();
            if (!(manager is null))
            {
                if (!(manager.worldMapDlg is null))
                {
                    capi.World.Logger.Event("<cartography> <player spawn event> map dialog has elements count [" + manager.worldMapDlg.Composers.Count().ToString() + "]");
                }
                else
                {
                    capi.World.Logger.Event("<cartography> <player spawn event> map dialog was not ready");
                }

            }
        }

        private void onViewChangedClientOverrideDebug(List<Vec2i> nowVisible, List<Vec2i> nowHidden)
        {
            
            WorldMapManager WorldMapManager = capi.ModLoader.GetModSystem<WorldMapManager>();

            IWorldChunk ch = capi.World.GetBlockAccessor(false, false, false, false).GetChunkAtBlockPos(capi.World.Player.Entity.Pos.AsBlockPos);
            GuiElementMap mapElem = WorldMapManager.worldMapDlg.Composers.First().Value.GetElement("mapElem") as GuiElementMap;
            var start = capi.World.Player.Entity.Pos;
            
            ItemSlot slotWithMap = null;
            hasMap = ((!capi.World.Player.Entity.LeftHandItemSlot.Empty && capi.World.Player.Entity.LeftHandItemSlot.Itemstack.Item is Wand) ||
                (!capi.World.Player.Entity.RightHandItemSlot.Empty && capi.World.Player.Entity.RightHandItemSlot.Itemstack.Item is Wand));
            bool lostMapNow = this.hadMapLast == true & hasMap == false;
            bool gotMapNow = this.hadMapLast == false & hasMap == true;
            bool hadMapLast = hasMap;
            int wandUUIDNow = -1;
            if (hasMap)
            {
                if((!capi.World.Player.Entity.LeftHandItemSlot.Empty && capi.World.Player.Entity.LeftHandItemSlot.Itemstack.Item is Wand))
                {
                    slotWithMap = capi.World.Player.Entity.LeftHandItemSlot;
                } else if ((!capi.World.Player.Entity.RightHandItemSlot.Empty && capi.World.Player.Entity.RightHandItemSlot.Itemstack.Item is Wand))
                {
                    slotWithMap = capi.World.Player.Entity.RightHandItemSlot;
                }
                //get map UUID to see if its initialised :
                int wandUUID = Wand.getWandMapUUID(capi, slotWithMap);
                if (wandUUID == -1)
                {
                    //map isnt ready/initalised so we assume it doesnt exist actually.
                    hasMap = false;
                    gotMapNow = false;
                    hadMapLast = false;
                }
            }
            if(hasMap)
            {
                wandUUIDNow = Wand.getWandMapUUID(capi, slotWithMap);
            }
            bool differentWandNow = false;
            if(hadMapLast && hasMap && wandUUIDLast != wandUUIDNow)
            {
                differentWandNow = true;
                wandUUIDLast = wandUUIDNow;
            }
            
            //could fetch the wands chunks now - would be more effecient? maybe
            ArchCartography ArchCartography = capi.ModLoader.GetModSystem<ArchCartography>();
            IClientNetworkChannel clientChannel = capi.Network.GetChannel("worldmap");
            //capi.World.Logger.Event("<cartography> on override MAP ELEMENT ViewChangedClient");
            //capi.World.Logger.Event("<cartography> what is now visible (count=[" + nowVisible.Count().ToString() + "])");
            //capi.World.Logger.Event("<cartography> what is now hidden (count=[" + nowHidden.Count().ToString() + "])");
            List<MapLayer> MapLayers = WorldMapManager.MapLayers;
            BlockPos pos = capi.World.Player.Entity.Pos.AsBlockPos;
            int _chunkSize = capi.World.BlockAccessor.ChunkSize;
            Vec2i chunkPos = new Vec2i().Set((int)(pos.X / _chunkSize), (int)(pos.Z / _chunkSize));
            bool differentChunkNow = chunkPosLast == null || (chunkPosLast != null && (chunkPos.X != chunkPosLast.X && chunkPos.Y != chunkPosLast.Y));
            chunkPosLast = chunkPos;
            //capi.World.Logger.Event("<cartography> player @ (" + pos.X.ToString() + ", " + pos.Z.ToString() + ")\n"
            //    + "...Which might be chunk (" + chunkPos.X.ToString() + ", " + chunkPos.Y.ToString() + ")");
            if (!hasMap)
            {
                foreach (Vec2i chunk in allRevealedChunks)
                {
                    if(!nowHidden.Contains(chunk))
                        nowHidden.Add(chunk);
                }
                allRevealedChunks.Clear();
            } else
            {
                if(isLocalCopyDirty)
                    ArchCartography.allRevealedChunks = Wand.getChunksForWandMap(wandUUIDNow,capi);
            }
                
                
            foreach (Vec2i visible in nowVisible)
            {

                if (allRevealedChunks.Contains(visible)) // (visible.X == chunkPos.X && visible.Y == chunkPos.Y) || 
                {
                    capi.World.Logger.Event("<cartography> will not hide intentionally chunk (" + chunkPos.X.ToString() + ", " + chunkPos.Y.ToString() + ")");
                }
                else
                {
                    //this.allTouchedChunks.Add(visible);
                    nowHidden.Add(visible);
                }
            }
            //foreach (Vec2i wastouched in allTouchedChunks)
           // {
           //     if (!nowHidden.Contains(wastouched)) // && wastouched.X != chunkPos.X && wastouched.Y != chunkPos.Y
           //         nowHidden.Add(wastouched);
           // }

            nowVisible.Clear();
            //nowVisible.Add(new Vec2i().Set(chunkPos.X, chunkPos.Y));
            foreach (Vec2i force_visible_chunk in allRevealedChunks)
            {
                nowVisible.Add(force_visible_chunk);
                if (nowHidden.Contains(force_visible_chunk))
                    nowHidden.Remove(force_visible_chunk);
            }
            int times = -1;
            foreach (MapLayer layer in MapLayers)
            {
                times++;
                //layer.LoadedChunks.Clear();
                if (times == 0)
                    capi.World.Logger.Event("<cartography> is mapLayer as RGBMapLayer visible property: " + ((RGBMapLayer)layer).Visible.ToString() + "? Title: " + ((ChunkMapLayer)layer).Title);
                if (times == 0)
                {
                    ChunkMapLayer chunkMapLayer = (ChunkMapLayer)layer;
                    if (isLocalCopyDirty && (lostMapNow || gotMapNow || differentChunkNow || differentWandNow))
                     {
                        isLocalCopyDirty = false;
                        //would rather do on dirty chunk pos or item changed (lost map)
                        clearLoadedMapData(chunkMapLayer);
                        clearChunkMapLayerChunksToGenerate(chunkMapLayer);
                    }
                    //chunkMapLayer

                }
                layer.OnViewChangedClient(nowVisible, nowHidden);
            }
            clientChannel.SendPacket(new OnViewChangedPacket() { NowVisible = nowVisible, NowHidden = nowHidden });

        }

        public UniqueQueue<Vec2i> GetChunkMapLayerChunksToGenerate(ChunkMapLayer cml)
        {
            var original = typeof(ChunkMapLayer).GetField("chunksToGen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return ((UniqueQueue<Vec2i>)original.GetValue(cml));
        }

        public ConcurrentDictionary<Vec2i, MultiChunkMapComponent> GetChunkMapLayerLoadedMapData(ChunkMapLayer cml)
        {
            var original = typeof(ChunkMapLayer).GetField("loadedMapData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            
            return ((ConcurrentDictionary< Vec2i, MultiChunkMapComponent >)original.GetValue(cml));
        }

        public void clearLoadedMapData(ChunkMapLayer cml)
        {
            System.Reflection.FieldInfo field = cml.GetType().GetField("loadedMapData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            ConcurrentDictionary<Vec2i, MultiChunkMapComponent> cd = (ConcurrentDictionary<Vec2i, MultiChunkMapComponent>)field.GetValue(cml);
            foreach (MultiChunkMapComponent cmp in cd.Values)
            {
                cmp.ActuallyDispose();
            }
            cd.Clear();
        }

        public void clearChunkMapLayerChunksToGenerate(ChunkMapLayer cml)
        {
            System.Reflection.FieldInfo field = cml.GetType().GetField("chunksToGen", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            UniqueQueue<Vec2i> original = (UniqueQueue < Vec2i > )field.GetValue(cml);
            original.Clear();
        }
    }
}
