using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace archcartography.src
{
    class MapPageBlockEntity : BlockEntity
    {
        public int UUID;
        public ICoreClientAPI capi;
        public ICoreServerAPI sapi;
        public BlockPos pos;


        public MapPageBlockEntity(BlockPos blockPos) : base()
        {
            this.pos = blockPos;
        }

        public MapPageBlockEntity(bool isUnique, bool isPaper, int pageMax, string title, string author, string[] text, BlockPos blockPos) : base()
        {
            this.pos = blockPos;
        }

        public MapPageBlockEntity(ICoreServerAPI sapi) : base()
        {
            //DeletingText();
            ArchCartography ArchCartography = sapi.ModLoader.GetModSystem<ArchCartography>();
            this.sapi = sapi;
            if(this.UUID == -1) this.UUID = ArchCartography.GetNextUUID(sapi);
            sapi.World.Logger.Event("<s> MapPageBlock UUID [" + this.UUID.ToString() + "]");
        }
        public MapPageBlockEntity(ICoreClientAPI capi) : base()
        {
            //DeletingText();
            this.capi = capi;
            capi.World.Logger.Event("<c> MapPageBlock UUID [" + this.UUID.ToString() + "]");

        }

        public MapPageBlockEntity() : base() { }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            // TODO: rewrite to only send data on read
            // only always load title info!
            UUID = tree.GetInt("UUID", -1);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetInt("UUID", this.UUID);
            Api.World.Logger.Event("<s:to> MapPageBlock UUID [" + this.UUID.ToString() + "]");

        }
    }
}
