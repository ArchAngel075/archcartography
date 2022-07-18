using ProtoBuf;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace archcartography.src
{
    public class WandMapDB : SQLiteDBConnection
    {
        public new string databaseFileName = "wandschunks";
        public override string DBTypeCode => "worldmap database";
        public ICoreAPI api;
        public WandMapDB(ILogger logger, ICoreAPI api) : base(logger)
        {
            this.api = api;
        }

        SQLiteCommand getChunksByWandIdCmd;
        SQLiteCommand setChunkByWandIdCmd;
        SQLiteCommand hasChunkByWandIdCmd;
        SQLiteCommand deleteChunkByWandIdCmd;
        SQLiteCommand getMaxWandIdCmd;
        SQLiteCommand hasWandsCmd;


        public override void OnOpened()
        {
            base.OnOpened();

            getChunksByWandIdCmd = sqliteConn.CreateCommand();
            getChunksByWandIdCmd.CommandText = "SELECT chunkx, chunky FROM wand_chunk WHERE wandid=@wand";
            getChunksByWandIdCmd.Parameters.Add("@wand", DbType.Int32, 1);
            getChunksByWandIdCmd.Prepare();

            hasChunkByWandIdCmd = sqliteConn.CreateCommand();
            hasChunkByWandIdCmd.CommandText = "SELECT COUNT(*) FROM wand_chunk WHERE wandid=@wand AND chunkx=@x AND chunky=@y";
            hasChunkByWandIdCmd.Parameters.Add("@wand", DbType.Int32, 1);
            hasChunkByWandIdCmd.Parameters.Add("@x", DbType.Int32, 1);
            hasChunkByWandIdCmd.Parameters.Add("@y", DbType.Int32, 1);
            hasChunkByWandIdCmd.Prepare();

            setChunkByWandIdCmd = sqliteConn.CreateCommand();
            setChunkByWandIdCmd.CommandText = "INSERT OR REPLACE INTO wand_chunk (wandid, chunkx, chunky) VALUES (@wand, @x, @y)";
            setChunkByWandIdCmd.Parameters.Add("@wand", DbType.Int32, 1);
            setChunkByWandIdCmd.Parameters.Add("@x", DbType.Int32, 1);
            setChunkByWandIdCmd.Parameters.Add("@y", DbType.Int32, 1);
            setChunkByWandIdCmd.Prepare();

            deleteChunkByWandIdCmd = sqliteConn.CreateCommand();
            deleteChunkByWandIdCmd.CommandText = "delete FROM wand_chunk WHERE wandid=@wand AND chunkx=@x AND chunky=@y";
            deleteChunkByWandIdCmd.Parameters.Add("@wand", DbType.Int32, 1);
            deleteChunkByWandIdCmd.Parameters.Add("@x", DbType.Int32, 1);
            deleteChunkByWandIdCmd.Parameters.Add("@y", DbType.Int32, 1);
            deleteChunkByWandIdCmd.Prepare();

            getMaxWandIdCmd = sqliteConn.CreateCommand();
            getMaxWandIdCmd.CommandText = "select max(wandid) from wand_chunk";
            getMaxWandIdCmd.Prepare();

            hasWandsCmd = sqliteConn.CreateCommand();
            hasWandsCmd.CommandText = "select count(wandid) > 0 from wand_chunk";
            hasWandsCmd.Prepare();


        }

        public bool HasWands()
        {
            using (SQLiteDataReader sqlite_datareader = hasWandsCmd.ExecuteReader())
            {
                if (sqlite_datareader.Read())
                {
                    bool has = sqlite_datareader.GetBoolean(0);
                    api.Logger.Event("has wands is [" + has.ToString() + "]");
                    return has;
                } else
                {
                    return false;
                }
            }
        }

        public int GetNextUUID()
        {
            if(api is ICoreClientAPI)
            {
                api.Logger.Event("Client attempted to create a new UUID for map object in Maps Database");
                //throw new System.Exception("Client attempted to create a new UUID for map object in Maps Database");
                return -1;
            }
            if(HasWands())
                using (SQLiteDataReader sqlite_datareader = getMaxWandIdCmd.ExecuteReader())
                {
                    if (sqlite_datareader.Read())
                    {
                        int? val = sqlite_datareader.GetInt32(0);
                        api.Logger.Event("val is [" + val.ToString() + "]");
                        if (val == null)
                        {
                            return 0;
                        }
                        int dataCount = (int)val;
                        return dataCount+1;
                    }
                    else
                    {
                        return 0;
                    }
                }
            return 0;
        }

        public struct ChunkSyncResult {
            public int added;
            public int removed;
        }

        /// <summary>
        /// Syncs to DB those chunks against that map. Removes and Adds as needed so that map is associated exactly with <param name="chunks">chunks to sync</param>
        /// </summary>
        /// <param name="chunks">chunks to sync</param>
        /// <param name="mapUUID">map UUID in DB to sync against</param>
        /// <returns></returns>
        public ChunkSyncResult SyncMapChunksToDB(List<Vec2i> chunks, int mapUUID)
        {
            int countAdded = 0;
            int countRemoved = 0;
            List<Vec2i> chunksNow = GetChunksOf(mapUUID);
            foreach (Vec2i chunkNow in chunksNow)
            {
                if (!chunks.Contains(chunkNow))
                {
                    if(DeleteChunkFromWand(mapUUID, chunkNow));
                        countRemoved++;
                }
            }
            foreach (Vec2i chunkToAdd in chunks)
            {
                if (!chunksNow.Contains(chunkToAdd))
                {
                    if (AddChunkToWand(mapUUID, chunkToAdd)) ;
                        countAdded++;
                }
            }
            return new ChunkSyncResult() { added = countAdded, removed = countRemoved };
            
        }

        public bool DeleteChunkFromWand(int wandID, Vec2i chunk)
        {
            deleteChunkByWandIdCmd.Parameters["@wand"].Value = wandID;
            deleteChunkByWandIdCmd.Parameters["@x"].Value = chunk.X;
            deleteChunkByWandIdCmd.Parameters["@y"].Value = chunk.Y;
            return deleteChunkByWandIdCmd.ExecuteNonQuery() == 1;
        }

        public bool HasWandChunk(int wandID, Vec2i chunk)
        {
            hasChunkByWandIdCmd.Parameters["@wand"].Value = wandID;
            hasChunkByWandIdCmd.Parameters["@x"].Value = chunk.X;
            hasChunkByWandIdCmd.Parameters["@y"].Value = chunk.Y;
            using (SQLiteDataReader sqlite_datareader = hasChunkByWandIdCmd.ExecuteReader())
            {
                if(sqlite_datareader.Read())
                {
                    int dataCount = sqlite_datareader.GetInt32(0);
                    return dataCount != null && ((long)dataCount) == 1 && ((long)dataCount) < 2;
                } else
                {
                    return false;
                }

            }
        }

        protected override void CreateTablesIfNotExists(SQLiteConnection sqliteConn)
        {
            using (SQLiteCommand sqlite_cmd = sqliteConn.CreateCommand())
            {
                sqlite_cmd.CommandText = "CREATE TABLE IF NOT EXISTS wand_chunk (wandid integer, chunkx integer, chunky integer);";
                sqlite_cmd.ExecuteNonQuery();
            }
        }

        public void Purge()
        {
            using (SQLiteCommand cmd = sqliteConn.CreateCommand())
            {
                cmd.CommandText = "delete FROM wand_chunk";
                cmd.ExecuteNonQuery();
            }
        }

        public List<Vec2i> GetChunksOf(int wandID)
        {
            List<Vec2i> chunksOut = new List<Vec2i>();
            getChunksByWandIdCmd.Parameters["@wand"].Value = wandID;
            using (SQLiteDataReader sqlite_datareader = getChunksByWandIdCmd.ExecuteReader()) 
            {
                while (sqlite_datareader.Read())
                {
                    object datax = sqlite_datareader["chunkx"];
                    object datay = sqlite_datareader["chunky"];
                    if (datax == null || datay == null) 
                        continue;

                    chunksOut.Add(new Vec2i(sqlite_datareader.GetInt32(0), sqlite_datareader.GetInt32(1)));
                }
            }

            return chunksOut;
        }

        public bool AddChunkToWand(int wandID, Vec2i chunk)
        {
            setChunkByWandIdCmd.Parameters["@wand"].Value = wandID;
            setChunkByWandIdCmd.Parameters["@x"].Value = chunk.X;
            setChunkByWandIdCmd.Parameters["@y"].Value = chunk.Y;
            return setChunkByWandIdCmd.ExecuteNonQuery() == 1;
        }

        public int AddChunksToWand(int mapID, Vec2i[] chunks)
        {
            int countAdded = 0;
            foreach (Vec2i chunk in chunks)
            {
                if (AddChunkToWand(mapID, chunk))
                    countAdded++;
            }
            
            return countAdded;
        }

        public int RemoveChunksFromWand(int mapID, Vec2i[] chunks)
        {
            int countRemoved = 0;
            foreach (Vec2i chunk in chunks)
            {
                if (DeleteChunkFromWand(mapID, chunk))
                    countRemoved++;
            }

            return countRemoved;
        }

        public override void Close()
        {
            getChunksByWandIdCmd?.Dispose();
            setChunkByWandIdCmd?.Dispose();
            hasChunkByWandIdCmd?.Dispose();
            deleteChunkByWandIdCmd?.Dispose();
            getMaxWandIdCmd?.Dispose();
            hasWandsCmd?.Dispose();

            base.Close();
        }


        public override void Dispose()
        {
            getChunksByWandIdCmd?.Dispose();
            setChunkByWandIdCmd?.Dispose();
            hasChunkByWandIdCmd?.Dispose();
            deleteChunkByWandIdCmd?.Dispose();
            getMaxWandIdCmd?.Dispose();
            hasWandsCmd?.Dispose();

            base.Dispose();
        }
    }
}
