using System.Text;
using System.Reflection;
using Terraria;
using Terraria.ID;
using TShockAPI;
using TShockAPI.DB;
using static LookBag.Plugin;
using static LookBag.Utils;

namespace LookBag;

// 玩家信息存储（用于Get指令的备份恢复）
internal class PlayerInfo
{
    public PlayerData? Backup { get; set; }
    public string CopyName { get; set; }
    public int UserID { get; set; }
    public PlayerInfo() { Backup = null; CopyName = ""; UserID = 0; }
    public bool Restore(TSPlayer plr)
    {
        if (Backup == null) return false;
        Backup.RestoreCharacter(plr);
        Backup = null; CopyName = ""; UserID = 0;
        return true;
    }
}

// 假玩家类（用于离线背包查询）
internal class FakeP : TSPlayer
{
    public FakeP() : base(string.Empty)
    {
        this.Account = new UserAccount();
    }
    public Player Player
    {
        get => this.TPlayer;
        set => typeof(TSPlayer).GetField("FakePlayer",
            BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(this, value);
    }
}

internal class MyCmd
{
    #region 指令参数
    public static string cmd => "bag";
    public static string prem => $"{cmd}.use";
    public static bool IsAdmin(TSPlayer plr) => plr.HasPermission($"{cmd}.admin");
    public static bool InGame(TSPlayer plr)
    {
        if (!plr.RealPlayer)
        {
            plr.SendMessage($"请进入游戏后再使用{PluginName}的{cmd}指令", color);
            return false;
        }
        return true;
    }
    private static PlayerInfo GetInfo(TSPlayer plr)
    {
        const string key = "查背包";
        if (!plr.ContainsData(key)) plr.SetData(key, new PlayerInfo());
        return plr.GetData<PlayerInfo>(key);
    }
    #endregion

    #region 待恢复字典（死亡玩家延迟恢复）
    private static Dictionary<string, PlayerData> Pending = new();

    // 玩家更新事件中调用（复活后恢复）
    public static void CheckPending(TSPlayer plr)
    {
        if (plr.Dead) return;
        if (Pending.TryGetValue(plr.Name, out var data))
        {
            data.RestoreCharacter(plr);
            Pending.Remove(plr.Name);
            plr.SendSuccessMessage("你的背包已被管理员修改并恢复。");
        }
    }

    // 玩家离开时自动恢复备份并清理待恢复数据
    public static void OnLeave(TSPlayer plr)
    {
        // 1. 如果管理员有未恢复的备份，自动恢复
        var info = GetInfo(plr);
        if (info.Backup != null)
        {
            info.Restore(plr);
            TShock.Log.ConsoleInfo($"[{PluginName}] 玩家 {plr.Name} 离开时自动恢复了背包。");
        }

        // 2. 如果该玩家有待恢复数据（死亡状态延迟恢复），直接写入离线数据库
        if (Pending.TryGetValue(plr.Name, out var data))
        {
            if (SaveDBData(plr.Name, data))
            {
                TShock.Log.ConsoleInfo($"[{PluginName}] 玩家 {plr.Name} 离开时已将延迟恢复数据写入数据库。");
            }
            else
            {
                TShock.Log.ConsoleError($"[{PluginName}] 玩家 {plr.Name} 的延迟恢复数据落库失败。");
            }
            Pending.Remove(plr.Name);
        }
    }
    #endregion

    #region 离线玩家数据获取与保存
    private static Player? OffGet(string name)
    {
        var acc = TShock.UserAccounts.GetUserAccountByName(name);
        if (acc == null) return null;
        var fake = new FakeP();
        fake.Account.ID = acc.ID;
        fake.Player = new Player { name = acc.Name };
        var data = TShock.CharacterDB.GetPlayerData(fake, acc.ID);
        if (data == null) return null;
        try { data.RestoreCharacter(fake); }
        catch { return null; }
        return fake.Player;
    }

    private static PlayerData? GetDBData(string name, TSPlayer plr)
    {
        var acc = TShock.UserAccounts.GetUserAccountByName(name);
        if (acc == null) return null;
        return TShock.CharacterDB.GetPlayerData(plr, acc.ID);
    }

    private static bool SaveDBData(string name, PlayerData data)
    {
        var acc = TShock.UserAccounts.GetUserAccountByName(name);
        if (acc == null) return false;
        try
        {
            string q = "UPDATE tsCharacter SET Health = @0, MaxHealth = @1, Mana = @2, MaxMana = @3, Inventory = @4 WHERE Account = @5;";
            TShock.CharacterDB.database.Query(q, data.health, data.maxHealth, data.mana, data.maxMana, string.Join("~", data.inventory), acc.ID);
            return true;
        }
        catch { return false; }
    }
    #endregion

    #region 菜单
    private static void Help(TSPlayer plr)
    {
        var sb = new StringBuilder();
        if (plr.RealPlayer)
            sb.AppendLine($"\n{ItemIcon(ItemID.NebulaPickup3)}[c/AD89D5:查][c/D68ACA:询][c/DF909A:背][c/E5A894:包]{ItemIcon(ItemID.NebulaPickup2)} {ItemIcon(ItemID.FragmentVortex)}[c/F2F2C7:重构] [c/BFDFEA:by] [c/00FFFF:羽学] {ItemIcon(ItemID.FragmentStardust)}");
        else
            sb.AppendLine($"\n《{PluginName}》");
        sb.AppendLine($"/{cmd} 玩家   --查询玩家背包（支持离线）");

        if (IsAdmin(plr))
        {
            sb.AppendLine($"/{cmd} g 玩家 --打开玩家背包（并备份自己）");
            sb.AppendLine($"/{cmd} s 玩家 --修改玩家背包（支持离线/死亡延迟）");
            sb.AppendLine($"/{cmd} 恢复自己背包");
        }
        SendMess(plr, sb.ToString());
    }
    #endregion

    #region 主指令
    internal static void MainCmd(CommandArgs args)
    {
        var plr = args.Player;
        if (args.Parameters.Count == 0)
        {
            var info = GetInfo(plr);
            if (info.Restore(plr))
                plr.SendSuccessMessage("已恢复你原来的背包。");
            else
                Help(plr);
            return;
        }
        var p0 = args.Parameters[0].ToLower();
        switch (p0)
        {
            case "g":
                if (args.Parameters.Count >= 2)
                    Get(plr, args.Parameters[1]);
                else
                    List(plr);
                break;
            case "s":
                if (args.Parameters.Count >= 2)
                    Set(plr, args.Parameters[1]);
                else
                    Help(plr);
                break;
            default:
                Query(plr, args.Parameters[0]);
                break;
        }
    }
    #endregion

    #region 列出所有玩家
    private static void List(TSPlayer plr)
    {
        if (!IsAdmin(plr)) { plr.SendErrorMessage("权限不足"); return; }

        var online = TShock.Players.Where(p => p != null && p.Active && p.RealPlayer).Select(p => p.Name).ToList();
        var users = TShock.UserAccounts.GetUserAccounts();
        var names = users.Where(u => !online.Contains(u.Name)).Select(u => u.Name).ToList();

        var all = online.Concat(names).OrderBy(n => n).ToList();
        if (all.Count == 0) { plr.SendInfoMessage("暂无任何玩家数据。"); return; }

        var sb = new StringBuilder();
        sb.AppendLine($"{ItemIcon(ItemID.NebulaPickup2)} [c/FFD966:玩家列表] (在线/离线):");

        int per = 10;
        for (int i = 0; i < all.Count; i++)
        {
            sb.Append($"{i}.{all[i]} ");
            if ((i + 1) % per == 0) sb.AppendLine();
        }
        if (all.Count % per != 0) sb.AppendLine();
        SendMess(plr, sb.ToString());
    }
    #endregion

    #region 查询背包（支持离线）
    private static void Query(TSPlayer plr, string name)
    {
        // 尝试在线
        var tar = FindPlayer(name);
        Player? player = null;
        bool offline = false;
        if (tar != null)
            player = tar.TPlayer;
        else
        {
            player = OffGet(name);
            offline = true;
            if (player == null)
            {
                plr.SendErrorMessage($"未找到玩家: {name}");
                return;
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"{ItemIcon(ItemID.NebulaPickup2)} [c/FFD966:{player.name}] 的背包{(offline ? "（离线）" : string.Empty)}:");

        var zones = new (string name, int start, int cnt)[]
        {
            ("主背包", 0, NetItem.InventorySlots),
            ("盔甲", NetItem.InventorySlots, NetItem.ArmorSlots),
            ("装备染料", NetItem.InventorySlots + NetItem.ArmorSlots, NetItem.DyeSlots),
            ("额外装备", NetItem.InventorySlots + NetItem.ArmorSlots + NetItem.DyeSlots, NetItem.MiscEquipSlots),
            ("额外染料", NetItem.InventorySlots + NetItem.ArmorSlots + NetItem.DyeSlots + NetItem.MiscEquipSlots, NetItem.MiscDyeSlots),
            ("猪猪罐", NetItem.PiggyIndex.Item1, NetItem.PiggySlots),
            ("保险箱", NetItem.SafeIndex.Item1, NetItem.SafeSlots),
            ("垃圾桶", NetItem.TrashIndex.Item1, NetItem.TrashSlots),
            ("熔炉", NetItem.ForgeIndex.Item1, NetItem.ForgeSlots),
            ("虚空保险库", NetItem.VoidIndex.Item1, NetItem.VoidSlots),
            ("配装1盔甲", NetItem.Loadout1Armor.Item1, NetItem.LoadoutArmorSlots),
            ("配装1染料", NetItem.Loadout1Dye.Item1, NetItem.LoadoutDyeSlots),
            ("配装2盔甲", NetItem.Loadout2Armor.Item1, NetItem.LoadoutArmorSlots),
            ("配装2染料", NetItem.Loadout2Dye.Item1, NetItem.LoadoutDyeSlots),
            ("配装3盔甲", NetItem.Loadout3Armor.Item1, NetItem.LoadoutArmorSlots),
            ("配装3染料", NetItem.Loadout3Dye.Item1, NetItem.LoadoutDyeSlots),
        };

        foreach (var zone in zones)
        {
            var items = new List<string>();
            for (int i = 0; i < zone.cnt; i++)
            {
                int slot = zone.start + i;
                var it = SlotBy(player, slot);
                if (it != null && it.type > 0 && it.stack > 0)
                    items.Add($"{i}.{ItemIcon(it.type, it.stack, it.prefix)}");
            }
            if (items.Count == 0) continue;

            sb.AppendLine($"{ItemIcon(ZoneIco(zone.name))} {zone.name}:");
            int per = 10;
            for (int idx = 0; idx < items.Count; idx++)
            {
                sb.Append(items[idx] + " ");
                if ((idx + 1) % per == 0) sb.AppendLine();
            }
            if (items.Count % per != 0) sb.AppendLine();
        }
        SendMess(plr, sb.ToString());
    }

    private static Item SlotBy(Player player, int slot)
    {
        if (slot < 0) return new Item();
        if (slot < NetItem.InventorySlots) return player.inventory[slot];
        int off = NetItem.InventorySlots;
        if (slot < off + NetItem.ArmorSlots) return player.armor[slot - off];
        off += NetItem.ArmorSlots;
        if (slot < off + NetItem.DyeSlots) return player.dye[slot - off];
        off += NetItem.DyeSlots;
        if (slot < off + NetItem.MiscEquipSlots) return player.miscEquips[slot - off];
        off += NetItem.MiscEquipSlots;
        if (slot < off + NetItem.MiscDyeSlots) return player.miscDyes[slot - off];
        if (slot < NetItem.PiggyIndex.Item2) return player.bank.item[slot - NetItem.PiggyIndex.Item1];
        if (slot < NetItem.SafeIndex.Item2) return player.bank2.item[slot - NetItem.SafeIndex.Item1];
        if (slot < NetItem.TrashIndex.Item2) return player.trashItem;
        if (slot < NetItem.ForgeIndex.Item2) return player.bank3.item[slot - NetItem.ForgeIndex.Item1];
        if (slot < NetItem.VoidIndex.Item2) return player.bank4.item[slot - NetItem.VoidIndex.Item1];
        if (slot < NetItem.Loadout1Armor.Item2) return player.Loadouts[0].Armor[slot - NetItem.Loadout1Armor.Item1];
        if (slot < NetItem.Loadout1Dye.Item2) return player.Loadouts[0].Dye[slot - NetItem.Loadout1Dye.Item1];
        if (slot < NetItem.Loadout2Armor.Item2) return player.Loadouts[1].Armor[slot - NetItem.Loadout2Armor.Item1];
        if (slot < NetItem.Loadout2Dye.Item2) return player.Loadouts[1].Dye[slot - NetItem.Loadout2Dye.Item1];
        if (slot < NetItem.Loadout3Armor.Item2) return player.Loadouts[2].Armor[slot - NetItem.Loadout3Armor.Item1];
        if (slot < NetItem.Loadout3Dye.Item2) return player.Loadouts[2].Dye[slot - NetItem.Loadout3Dye.Item1];
        return new Item();
    }

    private static int ZoneIco(string name) => name switch
    {
        "主背包" => ItemID.CopperPickaxe,
        "盔甲" => ItemID.IronHelmet,
        "装备染料" => ItemID.BlackDye,
        "额外装备" => ItemID.Shackle,
        "额外染料" => ItemID.BlackDye,
        "猪猪罐" => ItemID.PiggyBank,
        "保险箱" => ItemID.Safe,
        "垃圾桶" => ItemID.TrashCan,
        "熔炉" => ItemID.DefendersForge,
        "虚空保险库" => ItemID.VoidVault,
        "配装1盔甲" => ItemID.IronHelmet,
        "配装1染料" => ItemID.BlackDye,
        "配装2盔甲" => ItemID.IronHelmet,
        "配装2染料" => ItemID.BlackDye,
        "配装3盔甲" => ItemID.IronHelmet,
        "配装3染料" => ItemID.BlackDye,
        _ => ItemID.NebulaPickup2
    };
    #endregion

    #region 打开目标背包（Get）- 支持离线
    private static void Get(TSPlayer plr, string name)
    {
        if (!IsAdmin(plr)) { plr.SendErrorMessage("权限不足"); return; }
        if (!InGame(plr)) return;
        if (!Main.ServerSideCharacter) { plr.SendErrorMessage("需要开启SSC"); return; }

        var info = GetInfo(plr);
        if (info.Backup != null) info.Restore(plr);

        // 在线
        var tar = FindPlayer(name);
        if (tar != null && tar.Account != null)
        {
            info.Backup = new PlayerData(true);
            info.Backup.CopyCharacter(plr);
            info.CopyName = tar.Name;
            info.UserID = tar.Account.ID;

            var td = new PlayerData(true);
            td.CopyCharacter(tar);
            td.RestoreCharacter(plr);
            SendMess(plr, $"已打开 {tar.Name} 的背包。\n输入 /{cmd} 恢复自己。");
            return;
        }

        // 离线
        var off = GetDBData(name, plr);
        if (off == null) { plr.SendErrorMessage($"未找到玩家: {name}"); return; }

        info.Backup = new PlayerData(true);
        info.Backup.CopyCharacter(plr);
        info.CopyName = name;
        var acc = TShock.UserAccounts.GetUserAccountByName(name);
        info.UserID = acc?.ID ?? 0;

        off.RestoreCharacter(plr);
        SendMess(plr, $"已打开离线玩家 {name} 的背包。\n输入 /{cmd} 恢复自己");
    }
    #endregion

    #region 推送自己背包给目标（Set）- 支持离线与死亡延迟
    private static void Set(TSPlayer plr, string name)
    {
        if (!IsAdmin(plr)) { plr.SendErrorMessage("权限不足"); return; }
        if (!InGame(plr)) return;
        if (!Main.ServerSideCharacter) { plr.SendErrorMessage("需要开启SSC"); return; }

        // 在线
        var tar = FindPlayer(name);
        if (tar != null && tar.Account != null)
        {
            var myData = new PlayerData(true);
            myData.CopyCharacter(plr);

            if (tar.Dead)
            {
                // 延迟恢复
                Pending[tar.Name] = myData;
                plr.SendSuccessMessage($"已将你的背包复制给 {tar.Name}（他当前死亡，复活后生效）。");
                return;
            }

            var bak = new PlayerData(true);
            bak.CopyCharacter(tar);
            try
            {
                myData.RestoreCharacter(tar);
                plr.SendSuccessMessage($"已将你的背包复制给 {tar.Name}。");
            }
            catch (Exception ex)
            {
                bak.RestoreCharacter(tar);
                TShock.Log.ConsoleError($"[{PluginName}] Set失败: {ex.Message}");
                plr.SendErrorMessage("操作失败，已恢复目标数据。");
            }
            return;
        }

        // 离线
        var off = GetDBData(name, plr);
        if (off == null) { plr.SendErrorMessage($"未找到离线玩家: {name}"); return; }

        var myOff = new PlayerData(true);
        myOff.CopyCharacter(plr);
        off.inventory = myOff.inventory;
        off.health = myOff.health;
        off.maxHealth = myOff.maxHealth;
        off.mana = myOff.mana;
        off.maxMana = myOff.maxMana;

        if (SaveDBData(name, off))
            plr.SendSuccessMessage($"已将你的背包复制给离线玩家 {name}。");
        else
            plr.SendErrorMessage($"修改离线玩家 {name} 失败。");
    }
    #endregion

    #region 辅助查找玩家
    private static TSPlayer? FindPlayer(string name)
    {
        var match = TSPlayer.FindByNameOrID(name);
        if (match.Count == 0) return null;
        if (match.Count > 1)
        {
            var ex = match.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (ex != null) return ex;
        }
        return match[0];
    }
    #endregion
}