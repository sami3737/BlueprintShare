//Requires: Clans
using Oxide.Core;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("BlueprintShare", "sami37", "1.0.0")]
    public class BlueprintShare : RustPlugin
    {
        [PluginReference] private Plugin Clans;

        Dictionary<string, int> MembersCount = new Dictionary<string, int>();
        Dictionary<string, List<ulong>> PlayerBP = new Dictionary<string, List<ulong>>();

        void Loaded()
        {
            PlayerBP = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, List<ulong>>>(Name);
            if (Clans != null && Clans.IsLoaded)
            {
                JArray AllClans = (JArray) Clans.CallHook("GetAllClans");

                foreach (var clans in AllClans)
                {
                    List<BasePlayer> membersList = new List<BasePlayer>();
                    object clan = Clans?.Call<JObject>("GetClan", clans.ToString());

                    JToken members = (clan as JObject)?.GetValue("members");

                    if (members is JArray)
                    {
                        foreach (JToken member in (JArray)members)
                        {
                            ulong clanMemberUid;

                            if (!ulong.TryParse(member.ToString(), out clanMemberUid)) continue;

                            BasePlayer clanMember = RustCore.FindPlayerById(clanMemberUid);

                            membersList.Add(clanMember);
                        }
                    }
                    MembersCount.Add(clans.ToString(), membersList.Count);
                }

                var playerList = CollectPlayers();
                foreach (var player in playerList)
                {
                    if (InClan(player.userID))
                    {
                        List<BasePlayer> clan = GetClanMembers(player.userID);
                        foreach (var bp in PlayerBP.Where(x => x.Value.Contains(player.userID)))
                        {
                            ItemDefinition itemDefinition = GetItemDefinition(bp.Key);

                            if (itemDefinition == null) return;

                            foreach (var clanPlayer in clan)
                            {
                                if (clanPlayer.userID == player.userID)
                                    continue;

                                PlayerBlueprints blueprintComponent = clanPlayer.blueprints;
                                if (blueprintComponent == null) return;

                                blueprintComponent.Unlock(itemDefinition);
                            }
                        }
                    }
                }
            }
        }

        void Unload()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, PlayerBP);
            foreach (var player in BasePlayer.activePlayerList)
            {
                PlayerBlueprints blueprintComponent = player.blueprints;

                if (blueprintComponent == null) return;

                blueprintComponent.Reset();
                foreach (var blueprint in PlayerBP.Where(x => x.Value.Contains(player.userID)))
                {
                    ItemDefinition itemDefinition = GetItemDefinition(blueprint.Key);

                    if (itemDefinition == null) return;

                    blueprintComponent.Unlock(itemDefinition);
                }
            }
        }

        void OnSave()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, PlayerBP);
        }

        private List<BasePlayer> CollectPlayers()
        {
            List<BasePlayer> bpList = new List<BasePlayer>();
            foreach (var player in BasePlayer.activePlayerList)
            {
                bpList.Add(player);
            }

            foreach (var player in BasePlayer.sleepingPlayerList)
            {
                bpList.Add(player);
            }

            return bpList;
        }

        private List<BasePlayer> GetClanMembers(ulong playerUid)
        {
            List<BasePlayer> membersList = new List<BasePlayer>();

            string clanName = Clans?.Call<string>("GetClanOf", playerUid);

            if (clanName != null)
            {
                object clan = Clans?.Call<JObject>("GetClan", clanName);

                JToken members = (clan as JObject)?.GetValue("members");

                if (members is JArray)
                {
                    foreach (JToken member in (JArray) members)
                    {
                        ulong clanMemberUid;

                        if (!ulong.TryParse(member.ToString(), out clanMemberUid)) continue;

                        BasePlayer clanMember = RustCore.FindPlayerById(clanMemberUid);

                        membersList.Add(clanMember);
                    }
                }
            }

            return membersList;
        }

        public object GetClanOf(string name) => Clans.CallHook("GetClanOf", name);

        public object GetClan(string name) => Clans.CallHook("GetClan", name);

        private void OnItemAction(Item item, string action, BasePlayer player)
        {
            Puts(action);
            if (player != null && action == "study" && item.IsBlueprint()))
            {
                string itemShortName = item.blueprintTargetDef.shortname;

                if (string.IsNullOrEmpty(itemShortName)) return;

                item.Remove();

                UnlockBp(player, itemShortName);
                Puts("fff");
            }
        }

        private void UnlockBp(BasePlayer player, string itemShortName)
        {
            if (string.IsNullOrEmpty(itemShortName)) return;
            ulong playerUid = player.userID;

            if (!PlayerBP.ContainsKey(itemShortName))
            {
                PlayerBP.Add(itemShortName, new List<ulong>());
                PlayerBP[itemShortName].Add(playerUid);
                Puts("New entry");
            }
            else
            {
                if(!PlayerBP[itemShortName].Contains(playerUid))
                    PlayerBP[itemShortName].Add(playerUid);
                Puts("Already in");
            }
            Puts("vrfe");
            if (!InClan(player.userID)) return;


            List<BasePlayer> playersToShareWith = new List<BasePlayer>();

            if (Clans != null)
            {
                playersToShareWith.AddRange(GetClanMembers(playerUid));
            }

            foreach (BasePlayer sharePlayer in playersToShareWith)
            {
                if (sharePlayer != null)
                {
                    PlayerBlueprints blueprintComponent = sharePlayer.blueprints;

                    if (blueprintComponent == null) return;

                    ItemDefinition itemDefinition = GetItemDefinition(itemShortName);

                    if (itemDefinition == null) return;

                    blueprintComponent.Unlock(itemDefinition);

                    Effect soundEffect =
                        new Effect("assets/prefabs/deployable/research table/effects/research-success.prefab",
                            sharePlayer.transform.position, Vector3.zero);

                    EffectNetwork.Send(soundEffect, sharePlayer.net.connection);
                }
            }
        }

        private ItemDefinition GetItemDefinition(string itemShortName)
        {
            if (string.IsNullOrEmpty(itemShortName)) return null;

            ItemDefinition itemDefinition = ItemManager.FindItemDefinition(itemShortName.ToLower());

            return itemDefinition;
        }

        private bool InClan(ulong playerUid)
        {
            object clanName = Clans?.Call<string>("GetClanOf", playerUid);

            return clanName != null;
        }

        private void OnClanUpdate(string tag)
        {
            List<BasePlayer> clans = new List<BasePlayer>();

            object clan = Clans?.Call<JObject>("GetClan", tag);

            JToken members = ((JObject) clan)?.GetValue("members");

            if (members is JArray)
            {
                foreach (JToken member in (JArray)members)
                {
                    ulong clanMemberUid;

                    if (!ulong.TryParse(member.ToString(), out clanMemberUid)) continue;

                    BasePlayer clanMember = RustCore.FindPlayerById(clanMemberUid);

                    clans.Add(clanMember);
                }
            }

            if (MembersCount != null && MembersCount.ContainsKey(tag) && MembersCount[tag] != clans.Count)
            {
                foreach (var player in clans)
                {
                    PlayerBlueprints blueprintComponent = player.blueprints;

                    if (blueprintComponent == null) return;

                    blueprintComponent.Reset();

                    foreach (var blueprint in PlayerBP.Where(x => x.Value.Contains(player.userID)))
                    {
                        ItemDefinition itemDefinition = GetItemDefinition(blueprint.Key);

                        if (itemDefinition == null) return;

                        foreach (var ppl in clans)
                        {
                            PlayerBlueprints pplBp = ppl.blueprints;

                            if (pplBp == null) continue;

                            blueprintComponent.Unlock(itemDefinition);
                        }
                        blueprintComponent.Unlock(itemDefinition);
                    }
                }
            }
        }

        [ChatCommand("test")]
        void cmdChatTest(BasePlayer player, string cmd, string[] args)
        {

        }
    }
}