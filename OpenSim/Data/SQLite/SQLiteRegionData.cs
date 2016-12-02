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
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Data;
using RegionFlags = OpenSim.Framework.RegionFlags;
#if CSharpSqlite
    using Community.CsharpSqlite.Sqlite;
#else
    using Mono.Data.Sqlite;
#endif
using log4net;

namespace OpenSim.Data.SQLite
{
    public class SQLiteRegionData : SQLiteGenericTableHandler<RegionData>, IRegionData
    {
        protected virtual Assembly Assembly
        {
            get { return GetType().Assembly; }
        }

        public SQLiteRegionData(string connectionString, string realm)
                : base(connectionString, realm, "GridStore")
        {
        }

        public List<RegionData> Get(string regionName, UUID scopeID)
        {
            string command = "select * from `"+m_Realm+"` where regionName like :regionName";
            if (scopeID != UUID.Zero)
                command += " and ScopeID = :scopeID";

            command += " order by regionName";

            using (SqliteCommand cmd = new SqliteCommand(command))
            {
                cmd.Parameters.AddWithValue(":regionName", regionName);
                cmd.Parameters.AddWithValue(":scopeID", scopeID.ToString());

                return new List<RegionData>(DoQuery(cmd));
            }
        }

        public RegionData Get(int posX, int posY, UUID scopeID)
        {
/* fixed size regions
            string command = "select * from `"+m_Realm+"` where locX = :posX and locY = :posY";
            if (scopeID != UUID.Zero)
                command += " and ScopeID = :scopeID";

            using (SqliteCommand cmd = new SqliteCommand(command))
            {
                cmd.Parameters.AddWithValue(":posX", posX.ToString());
                cmd.Parameters.AddWithValue(":posY", posY.ToString());
                cmd.Parameters.AddWithValue(":scopeID", scopeID.ToString());

                RegionData ret = DoQuery(cmd)[0];
            }
*/
            // extend database search for maximum region size area
            string command = "select * from `" + m_Realm + "` where locX between :startX and :endX and locY between :startY and :endY";
            if (scopeID != UUID.Zero)
                command += " and ScopeID = :scopeID";

            int startX = posX - (int)Constants.MaximumRegionSize;
            int startY = posY - (int)Constants.MaximumRegionSize;
            int endX = posX;
            int endY = posY;

            List<RegionData> ret;
            using (SqliteCommand cmd = new SqliteCommand(command))
            {
                cmd.Parameters.AddWithValue(":startX", startX.ToString());
                cmd.Parameters.AddWithValue(":startY", startY.ToString());
                cmd.Parameters.AddWithValue(":endX", endX.ToString());
                cmd.Parameters.AddWithValue(":endY", endY.ToString());
                cmd.Parameters.AddWithValue(":scopeID", scopeID.ToString());

                ret = new List<RegionData>(DoQuery(cmd));
            }

            if (ret.Count == 0)
                return null;

            // find the first that contains pos
            RegionData rg = null;
            foreach (RegionData r in ret)
            {
                if (posX >= r.posX && posX < r.posX + r.sizeX
                    && posY >= r.posY && posY < r.posY + r.sizeY)
                {
                    rg = r;
                    break;
                }
            }

            return rg;
        }

        public RegionData Get(UUID regionID, UUID scopeID)
        {
            string command = "select * from `"+m_Realm+"` where uuid = :regionID";
            if (scopeID != UUID.Zero)
                command += " and ScopeID = :scopeID";

            using (SqliteCommand cmd = new SqliteCommand(command))
            {
                cmd.Parameters.AddWithValue(":regionID", regionID.ToString());
                cmd.Parameters.AddWithValue(":scopeID", scopeID.ToString());

                List<RegionData> ret = new List<RegionData>(DoQuery(cmd));
                if (ret.Count == 0)
                    return null;

                return ret[0];
            }
        }

        public List<RegionData> Get(int startX, int startY, int endX, int endY, UUID scopeID)
        {
/* fix size regions
            string command = "select * from `"+m_Realm+"` where locX between :startX and :endX and locY between :startY and :endY";
            if (scopeID != UUID.Zero)
                command += " and ScopeID = :scopeID";

            using (SqliteCommand cmd = new SqliteCommand(command))
            {
                cmd.Parameters.AddWithValue(":startX", startX.ToString());
                cmd.Parameters.AddWithValue(":startY", startY.ToString());
                cmd.Parameters.AddWithValue(":endX", endX.ToString());
                cmd.Parameters.AddWithValue(":endY", endY.ToString());
                cmd.Parameters.AddWithValue(":scopeID", scopeID.ToString());

                return DoQuery(cmd).ToList();
            }
 */
            string command = "select * from `" + m_Realm + "` where locX between :startX and :endX and locY between :startY and :endY";
            if (scopeID != UUID.Zero)
                command += " and ScopeID = :scopeID";

            int qstartX = startX - (int)Constants.MaximumRegionSize;
            int qstartY = startY - (int)Constants.MaximumRegionSize;

            List<RegionData> dbret;
            using (SqliteCommand cmd = new SqliteCommand(command))
            {
                cmd.Parameters.AddWithValue(":startX", qstartX.ToString());
                cmd.Parameters.AddWithValue(":startY", qstartY.ToString());
                cmd.Parameters.AddWithValue(":endX", endX.ToString());
                cmd.Parameters.AddWithValue(":endY", endY.ToString());
                cmd.Parameters.AddWithValue(":scopeID", scopeID.ToString());

                dbret = new List<RegionData>(DoQuery(cmd));
            }

            List<RegionData> ret = new List<RegionData>();

            if (dbret.Count == 0)
                return ret;

            foreach (RegionData r in dbret)
            {
                if (r.posX + r.sizeX > startX && r.posX <= endX
                    && r.posY + r.sizeY > startY && r.posY <= endY)
                    ret.Add(r);
            }
            return ret;
        }

        public bool SetDataItem(UUID regionID, string item, string value)
        {
            using (SqliteCommand cmd = new SqliteCommand("update `" + m_Realm + "` set `" + item + "` = :" + item + " where uuid = :UUID"))
            {
                cmd.Parameters.AddWithValue(":" + item, value);
                cmd.Parameters.AddWithValue(":UUID", regionID.ToString());

                if (cmd.ExecuteNonQuery() > 0)
                    return true;
            }

            return false;
        }

        public bool Delete(UUID regionID)
        {
            return Delete("UUID", regionID.ToString());
        }

        public List<RegionData> GetDefaultRegions(UUID scopeID)
        {
            return Get((int)RegionFlags.DefaultRegion, scopeID);
        }

        public List<RegionData> GetDefaultHypergridRegions(UUID scopeID)
        {
            return Get((int)RegionFlags.DefaultHGRegion, scopeID);
        }

        public List<RegionData> GetFallbackRegions(UUID scopeID, int x, int y)
        {
            List<RegionData> regions = Get((int)RegionFlags.FallbackRegion, scopeID);
            RegionDataDistanceCompare distanceComparer = new RegionDataDistanceCompare(x, y);
            regions.Sort(distanceComparer);
            return regions;
        }

        public List<RegionData> GetHyperlinks(UUID scopeID)
        {
            return Get((int)RegionFlags.Hyperlink, scopeID);
        }

        private List<RegionData> Get(int regionFlags, UUID scopeID)
        {
            string command = "select * from `" + m_Realm + "` where (flags & " + regionFlags.ToString() + ") <> 0";
            if (scopeID != UUID.Zero)
                command += " and ScopeID = :scopeID";

            using (SqliteCommand cmd = new SqliteCommand(command))
            {
                cmd.Parameters.AddWithValue(":scopeID", scopeID.ToString());
    
                return new List<RegionData>(DoQuery(cmd));
            }
        }
    }
}
