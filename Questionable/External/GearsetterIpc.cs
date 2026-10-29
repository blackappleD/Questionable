// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @blackappleD
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class GearsetterIpc(IDalamudPluginInterface pluginInterface, ILogger<GearsetterIpc> logger) : Ipc
{
    public override string InternalName => "Gearsetter";
    public override Version? GetVersion() => IPCSubscriber.Version(InternalName);
    public override bool IsReady() => IsInstalled;

    private readonly ICallGateSubscriber<byte, List<(uint ItemId, InventoryType? SourceInventory, byte? SourceInventorySlot, RaptureGearsetModule.GearsetItemIndex TargetSlot)>> _getRecommendationsForGearset =
        pluginInterface.GetIpcSubscriber<byte, List<(uint ItemId, InventoryType? SourceInventory, byte? SourceInventorySlot, RaptureGearsetModule.GearsetItemIndex TargetSlot)>>("Gearsetter.GetRecommendationsForGearset");

    public static bool IsInstalled => IPCSubscriber.IsInstalled("Gearsetter");

    public List<(uint ItemId, InventoryType? SourceInventory, byte? SourceInventorySlot, RaptureGearsetModule.GearsetItemIndex TargetSlot)>? GetRecommendationsForGearset(byte gearsetId)
    {
        try
        {
            return _getRecommendationsForGearset.InvokeFunc(gearsetId);
        }
        catch (IpcError e)
        {
            logger.LogWarning(e, "Could not query Gearsetter for gearset recommendations");
            return null;
        }
    }
}
