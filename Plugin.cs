using Terraria;
using Terraria.ID;
using TShockAPI;
using TShockAPI.Hooks;
using TerrariaApi.Server;
using static LookBag.Utils;
using Microsoft.Xna.Framework;

namespace LookBag;

[ApiVersion(2, 1)]
public class Plugin(Main game) : TerrariaPlugin(game)
{
    #region 插件信息
    public static string PluginName => "查询背包"; // 插件名称
    public override string Name => PluginName;
    public override string Author => "羽学";
    public override Version Version => new(1, 0, 0);
    public override string Description => "使用指令查询玩家背包，支持克隆玩家到自身修改后再还给玩家，并还原自身物品";
    #endregion

    #region 注册与释放
    public override void Initialize()
    {
        PlayerHooks.PlayerLogout += OnLogout;
        ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
        GetDataHandlers.PlayerUpdate.Register(OnPlayerUpdate);
        TShockAPI.Commands.ChatCommands.Add(new Command(MyCmd.prem, MyCmd.MainCmd, MyCmd.cmd));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            PlayerHooks.PlayerLogout -= OnLogout;
            ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
            GetDataHandlers.PlayerUpdate.UnRegister(OnPlayerUpdate);
            TShockAPI.Commands.ChatCommands.RemoveAll(x => x.CommandDelegate == MyCmd.MainCmd);
        }
        base.Dispose(disposing);
    }
    #endregion

    #region 玩家更新与离服事件
    private void OnPlayerUpdate(object? sender, GetDataHandlers.PlayerUpdateEventArgs e)
    {
        var plr = e.Player;
        if (plr == null || !plr.RealPlayer || !plr.Active || !plr.IsLoggedIn) return;
        MyCmd.CheckPending(plr);
    }

    private void OnLeave(LeaveEventArgs args)
    {
        var plr = TShock.Players[args.Who];
        if (plr == null) return;
        MyCmd.OnLeave(plr);
    }

    private void OnLogout(PlayerLogoutEventArgs e)
    {
        var plr = e.Player;
        if (plr != null && plr.Active && plr.RealPlayer)
            MyCmd.OnLeave(plr);
    }
    #endregion
}