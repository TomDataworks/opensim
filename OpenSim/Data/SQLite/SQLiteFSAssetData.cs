/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Reflection;
using System.Collections.Generic;
using System.Data;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using log4net;
using OpenMetaverse;
#if CSharpSqlite
    using Community.CsharpSqlite.Sqlite;
#else
    using Mono.Data.Sqlite;
#endif

namespace OpenSim.Data.SQLite
{
    public class SQLiteFSAssetData : IFSAssetDataPlugin
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected SqliteConnection m_Connection = null;
        protected string m_ConnectionString;
        protected string m_Table;
        protected Object m_connLock = new Object();
        private long m_ticksToEpoch;

        /// <summary>
        /// Number of days that must pass before we update the access time on an asset when it has been fetched
        /// Config option to change this is "DaysBetweenAccessTimeUpdates"
        /// </summary>
        private int DaysBetweenAccessTimeUpdates = 0;

        protected virtual Assembly Assembly
        {
            get { return GetType().Assembly; }
        }
        
        public SQLiteFSAssetData()
        {
        }

        #region IPlugin Members

        public string Version { get { return "1.0.0.0"; } }

        // Loads and initialises the SQLite storage plugin and checks for migrations
        public void Initialise(string connect, string realm, int UpdateAccessTime)
        {
            m_ticksToEpoch = new System.DateTime(1970, 1, 1).Ticks;
            m_ConnectionString = connect;
            m_Table = realm;

            DaysBetweenAccessTimeUpdates = UpdateAccessTime;

            try
            {
                OpenDatabase();

                Migration m = new Migration(m_Connection, Assembly, "FSAssetStore");
                m.Update();
            }
            catch (SqliteException e)
            {
                m_log.ErrorFormat("[FSASSETS]: Can't connect to database: {0}", e.Message.ToString());
            }
        }

        public void Initialise()
        {
            throw new NotImplementedException();
        }

        public void Dispose() { }

        public string Name
        {
            get { return "SQLite FSAsset storage engine"; }
        }

        #endregion

        private bool OpenDatabase()
        {
            try
            {
                m_Connection = new SqliteConnection(m_ConnectionString);

                m_Connection.Open();
            }
            catch (SqliteException e)
            {
                m_log.ErrorFormat("[FSASSETS]: Can't connect to database: {0}",
                        e.Message.ToString());

                return false;
            }

            return true;
        }

        #region IFSAssetDataPlugin Members

        public AssetMetadata Get(string id, out string hash)
        {
            hash = String.Empty;

            SqliteCommand cmd = m_Connection.CreateCommand();

            cmd.CommandText = String.Format("select id, name, description, type, hash, create_time, access_time, asset_flags from {0} where id = :id", m_Table);
            cmd.Parameters.AddWithValue(":id", id);

            IDataReader reader = cmd.ExecuteReader();

            if (!reader.Read())
            {
                reader.Close();
                cmd.Dispose();
                return null;
            }
            
            AssetMetadata meta = new AssetMetadata();

            hash = reader["hash"].ToString();

            meta.ID = id;
            meta.FullID = new UUID(id);

            meta.Name = reader["name"].ToString();
            meta.Description = reader["description"].ToString();
            meta.Type = (sbyte)Convert.ToInt32(reader["type"]);
            meta.ContentType = SLUtil.SLAssetTypeToContentType(meta.Type);
            meta.CreationDate = Util.ToDateTime(Convert.ToInt32(reader["create_time"]));
            meta.Flags = (AssetFlags)Convert.ToInt32(reader["asset_flags"]);

            int AccessTime = Convert.ToInt32(reader["access_time"]);

            reader.Close();

            UpdateAccessTime(AccessTime, cmd);

            cmd.Dispose();

            return meta;
        }

        private void UpdateAccessTime(int AccessTime, SqliteCommand cmd)
        {
            // Reduce DB work by only updating access time if asset hasn't recently been accessed
            // 0 By Default, Config option is "DaysBetweenAccessTimeUpdates"
            if (DaysBetweenAccessTimeUpdates > 0 && (DateTime.UtcNow - Utils.UnixTimeToDateTime(AccessTime)).TotalDays < DaysBetweenAccessTimeUpdates)
                return;

            int now = (int)((System.DateTime.Now.Ticks - m_ticksToEpoch) / 10000000);
            cmd.CommandText = String.Format("UPDATE {0} SET `access_time` = :access_time WHERE `id` = :id", m_Table);
            cmd.Parameters.AddWithValue(":access_time", now);

            cmd.ExecuteNonQuery();
        }

        public bool Store(AssetMetadata meta, string hash)
        {
            try
            {
                string oldhash;
                AssetMetadata existingAsset = Get(meta.ID, out oldhash);

                SqliteCommand cmd = m_Connection.CreateCommand();

                int now = (int)((System.DateTime.Now.Ticks - m_ticksToEpoch) / 10000000);
                cmd.Parameters.AddWithValue(":id", meta.ID);
                cmd.Parameters.AddWithValue(":name", meta.Name);
                cmd.Parameters.AddWithValue(":description", meta.Description);
                cmd.Parameters.AddWithValue(":type", meta.Type.ToString());
                cmd.Parameters.AddWithValue(":hash", hash);
                cmd.Parameters.AddWithValue(":asset_flags", meta.Flags);
                cmd.Parameters.AddWithValue(":access_time", now);

                if (existingAsset == null)
                {
                    cmd.CommandText = String.Format("insert into {0} (id, name, description, type, hash, asset_flags, create_time, access_time) values ( :id, :name, :description, :type, :hash, :asset_flags, :access_time, :access_time)", m_Table);

                    cmd.ExecuteNonQuery();

                    cmd.Dispose();

                    return true;
                }

                //cmd.CommandText = String.Format("update {0} set hash = :hash, access_time = :access_time where id = :id", m_Table);

                //ExecuteNonQuery(cmd);

                cmd.Dispose();
                return false;
            }
            catch(Exception e)
            {
                m_log.Error("[FSAssets] Failed to store asset with ID " + meta.ID);
		        m_log.Error(e.ToString());
                return false;
            }
        }

        /// <summary>
        /// Check if the assets exist in the database.
        /// </summary>
        /// <param name="uuids">The asset UUID's</param>
        /// <returns>For each asset: true if it exists, false otherwise</returns>
        public bool[] AssetsExist(UUID[] uuids)
        {
            if (uuids.Length == 0)
                return new bool[0];

            HashSet<UUID> exists = new HashSet<UUID>();

            string ids = "'" + string.Join("','", uuids) + "'";
            string sql = string.Format("select id from {1} where id in ({0})", ids, m_Table);

            using (SqliteCommand cmd = m_Connection.CreateCommand())
            {
                cmd.CommandText = sql;

                using (SqliteDataReader dbReader = cmd.ExecuteReader())
                {
                    while (dbReader.Read())
                    {
                        UUID id = DBGuid.FromDB(dbReader["ID"]);
                        exists.Add(id);
                    }
                }
            }

            bool[] results = new bool[uuids.Length];
            for (int i = 0; i < uuids.Length; i++)
                results[i] = exists.Contains(uuids[i]);
            return results;
        }

        public int Count()
        {
            SqliteCommand cmd = m_Connection.CreateCommand();

            cmd.CommandText = String.Format("select count(*) as count from {0}", m_Table);

            IDataReader reader = cmd.ExecuteReader();

            reader.Read();

            int count = Convert.ToInt32(reader["count"]);

            reader.Close();
            cmd.Dispose();

            return count;
        }

        public bool Delete(string id)
        {
            using (SqliteCommand cmd = m_Connection.CreateCommand())
            {
                cmd.CommandText = String.Format("delete from {0} where id = :id", m_Table);

                cmd.Parameters.AddWithValue(":id", id);

                cmd.ExecuteNonQuery();
            }

            return true;
        }

        public void Import(string conn, string table, int start, int count, bool force, FSStoreDelegate store)
        {
            SqliteConnection importConn;

            try
            {
                importConn = new SqliteConnection(conn);

                importConn.Open();
            }
            catch (SqliteException e)
            {
                m_log.ErrorFormat("[FSASSETS]: Can't connect to database: {0}",
                        e.Message.ToString());

                return;
            }

            int imported = 0;

            SqliteCommand cmd = importConn.CreateCommand();

            string limit = String.Empty;
            if (count != -1)
            {
                limit = String.Format(" limit {0},{1}", start, count);
            }
                
            cmd.CommandText = String.Format("select * from {0}{1}", table, limit);

            MainConsole.Instance.Output("Querying database");
            IDataReader reader = cmd.ExecuteReader();

            MainConsole.Instance.Output("Reading data");

            while (reader.Read())
            {
                if ((imported % 100) == 0)
                {
                    MainConsole.Instance.Output(String.Format("{0} assets imported so far", imported));
                }
    
                AssetBase asset = new AssetBase();
                AssetMetadata meta = new AssetMetadata();

                meta.ID = reader["id"].ToString();
                meta.FullID = new UUID(meta.ID);

                meta.Name = reader["name"].ToString();
                meta.Description = reader["description"].ToString();
                meta.Type = (sbyte)Convert.ToInt32(reader["assetType"]);
                meta.ContentType = SLUtil.SLAssetTypeToContentType(meta.Type);
                meta.CreationDate = Util.ToDateTime(Convert.ToInt32(reader["create_time"]));

                asset.Metadata = meta;
                asset.Data = (byte[])reader["data"];

                store(asset, force);

                imported++;
            }

            reader.Close();
            cmd.Dispose();
            importConn.Close();

            MainConsole.Instance.Output(String.Format("Import done, {0} assets imported", imported));
        }

        #endregion
    }
}
