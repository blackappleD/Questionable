using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.Interop;
using Questionable.Controller.Steps.Shared;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using static Questionable.Utils.LocalizeShortcut;
namespace Questionable.Controller.Steps.Interactions;

internal static class EquipRecommended
{
    internal sealed class Factory : SimpleTaskFactory
    {
        public override ITask? CreateTask(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.EquipRecommended)
                return null;

            return new EquipTask();
        }
    }

    internal sealed class BeforeDutyOrInstance(IObjectTable objectTable) : ITaskFactory
    {
        public IEnumerable<ITask> CreateAllTasks(Quest quest, QuestSequence sequence, QuestStep step)
        {
            if (step.InteractionType != EInteractionType.Duty &&
                step.InteractionType != EInteractionType.SinglePlayerDuty &&
                step.InteractionType != EInteractionType.Combat)
                yield break;

            // If step equips something, we probably want to leave the manually equipped item
            if (sequence.Steps.Any(x => x.InteractionType is EInteractionType.EquipItem))
                yield break;
            // If there's an unequipped job stone, find it and equip it
            if (objectTable[0] is IPlayerCharacter player &&
                ClassJobUtils.ClassToJobStone((Job)player.ClassJob.Value.RowId) is (Job targetJob, ushort jobStone))
            {
                bool hasJobStone = false;
                unsafe
                {
                    InventoryManager* inventoryManager = InventoryManager.Instance();
                    InventoryContainer* equippedContainer = inventoryManager->GetInventoryContainer(InventoryType.ArmorySoulCrystal);
                    if (inventoryManager->GetItemCountInContainer(jobStone, InventoryType.ArmorySoulCrystal) != 0)
                        hasJobStone = true;
                }
                if (hasJobStone && quest.GetQuestInfo().ClassJobs.Contains(targetJob))
                {
                    yield return new EquipItem.Task(jobStone);
                    yield return new CreateGearset.Task();
                }
            }
            yield return new EquipTask();
        }
    }

    internal sealed class EquipTask : ITask
    {
        public override string ToString() => "EquipRecommended";
    }

    internal sealed unsafe class DoEquipRecommended(IChatGui chatGui, ICondition condition, Configuration config, StylistIpc stylist, GearsetterIpc gearsetter, ILogger<DoEquipRecommended> logger)
        : TaskExecutor<EquipTask>
    {
        private bool _checkedOrTriggeredEquipmentUpdate;
        private DateTime _continueAt = DateTime.MinValue;

        private List<(uint ItemId, InventoryType? SourceInventory, byte? SourceInventorySlot, RaptureGearsetModule.GearsetItemIndex TargetSlot)>? _gearsetterRecommendations;
        private int _gearsetterIndex;
        private bool _gearsetterHadRecommendations;
        private bool _gearsetterSecondPassDone;

        protected override bool Start()
        {
            if (condition[ConditionFlag.InCombat])
                return false;

            if (!StylistIpc.IsInstalled && config.General.GearsetUpdateSource is EGearsetUpdateSource.Stylist)
            {
                chatGui.Print(_L("You've set Stylist to manage equipped gear, but it is not installed. Resetting to Vanilla."), CommandHandler.MessageTag, CommandHandler.TagColor);
                config.General.GearsetUpdateSource = EGearsetUpdateSource.Vanilla;
                Svc.PluginInterface.SavePluginConfig(config);
            }
            if (!GearsetterIpc.IsInstalled && config.General.GearsetUpdateSource is EGearsetUpdateSource.Gearsetter)
            {
                chatGui.Print(_L("You've set Gearsetter to manage equipped gear, but it is not installed. Resetting to Vanilla."), CommandHandler.MessageTag, CommandHandler.TagColor);
                config.General.GearsetUpdateSource = EGearsetUpdateSource.Vanilla;
                Svc.PluginInterface.SavePluginConfig(config);
            }
            switch (config.General.GearsetUpdateSource)
            {
                case EGearsetUpdateSource.Vanilla:
                    RecommendEquipModule.Instance()->SetupForClassJob(PlayerState.Instance()->CurrentClassJobId);
                    break;
                case EGearsetUpdateSource.Stylist:
                    RaptureGearsetModule.Instance()->UpdateGearset(RaptureGearsetModule.Instance()->CurrentGearsetIndex);
                    break;
                case EGearsetUpdateSource.Gearsetter:
                    // Gearsetter only recommends upgrades for a saved gearset
                    if (RaptureGearsetModule.Instance()->CurrentGearsetIndex < 0)
                    {
                        logger.LogWarning("No gearset is active, can't ask Gearsetter for recommendations");
                        return false;
                    }

                    // make sure the gearset matches what is currently equipped before asking Gearsetter
                    RaptureGearsetModule.Instance()->UpdateGearset(RaptureGearsetModule.Instance()->CurrentGearsetIndex);
                    _continueAt = DateTime.Now.AddSeconds(0.5);
                    break;
            }

            return true;
        }

        public override ETaskResult Update()
        {
            switch (config.General.GearsetUpdateSource)
            {
                case EGearsetUpdateSource.Vanilla:
                    RecommendEquipModule* recommendedEquipModule = RecommendEquipModule.Instance();
                    if (recommendedEquipModule->IsUpdating)
                        return ETaskResult.StillRunning;

                    if (!_checkedOrTriggeredEquipmentUpdate)
                    {
                        if (!IsAllRecommendedGearEquipped())
                        {
                            chatGui.Print(_L("Equipping recommended gear."), CommandHandler.MessageTag, CommandHandler.TagColor);
                            recommendedEquipModule->EquipRecommendedGear();
                            _continueAt = DateTime.Now.AddSeconds(0.25);
                        }

                        _checkedOrTriggeredEquipmentUpdate = true;
                        return ETaskResult.StillRunning;
                    }

                    break;
                case EGearsetUpdateSource.Stylist:
                    {
                        if (stylist.IsBusy)
                            return ETaskResult.StillRunning;

                        if (!_checkedOrTriggeredEquipmentUpdate)
                        {
                            stylist.UpdateGearset();
                            _checkedOrTriggeredEquipmentUpdate = true;
                            _continueAt = DateTime.Now.AddSeconds(0.25);
                            return ETaskResult.StillRunning;
                        }
                    }

                    break;
                case EGearsetUpdateSource.Gearsetter:
                    if (UpdateGearsetter() is { } result)
                        return result;

                    break;
            }

            return DateTime.Now >= _continueAt ? ETaskResult.TaskComplete : ETaskResult.StillRunning;
        }

        private ETaskResult? UpdateGearsetter()
        {
            if (DateTime.Now < _continueAt)
                return ETaskResult.StillRunning;

            RaptureGearsetModule* gearsetModule = RaptureGearsetModule.Instance();
            if (_gearsetterRecommendations == null)
            {
                _gearsetterRecommendations = gearsetter.GetRecommendationsForGearset((byte)gearsetModule->CurrentGearsetIndex) ?? [];
                _gearsetterIndex = 0;
                if (_gearsetterRecommendations.Count > 0 && !_gearsetterHadRecommendations)
                {
                    _gearsetterHadRecommendations = true;
                    chatGui.Print(_L("Equipping recommended gear."), CommandHandler.MessageTag, CommandHandler.TagColor);
                }

                return ETaskResult.StillRunning;
            }

            if (_gearsetterIndex < _gearsetterRecommendations.Count)
            {
                (uint itemId, InventoryType? sourceInventory, byte? sourceSlot, RaptureGearsetModule.GearsetItemIndex targetSlot) =
                    _gearsetterRecommendations[_gearsetterIndex++];
                if (sourceInventory == null || sourceSlot == null)
                    return ETaskResult.StillRunning;

                InventoryManager* inventoryManager = InventoryManager.Instance();
                InventoryContainer* sourceContainer = inventoryManager->GetInventoryContainer(sourceInventory.Value);
                InventoryItem* sourceItem = sourceContainer != null ? sourceContainer->GetInventorySlot(sourceSlot.Value) : null;
                if (sourceItem == null || sourceItem->ItemId != itemId)
                {
                    // Gearsetter returns the same slot twice if two copies of the same ring should be worn,
                    // the second pass below picks up the other one.
                    logger.LogInformation("Item {ItemId} is no longer in {Inventory} slot {Slot}", itemId, sourceInventory, sourceSlot);
                    return ETaskResult.StillRunning;
                }

                logger.LogInformation("Equipping item {ItemId} from {Inventory} slot {Slot} to {TargetSlot}", itemId, sourceInventory, sourceSlot, targetSlot);
                inventoryManager->MoveItemSlot(sourceInventory.Value, sourceSlot.Value, InventoryType.EquippedItems, (ushort)targetSlot, true);
                _continueAt = DateTime.Now.AddSeconds(0.1);
                return ETaskResult.StillRunning;
            }

            if (_gearsetterHadRecommendations && !_gearsetterSecondPassDone)
            {
                _gearsetterSecondPassDone = true;
                _gearsetterRecommendations = null;
                gearsetModule->UpdateGearset(gearsetModule->CurrentGearsetIndex);
                _continueAt = DateTime.Now.AddSeconds(0.5);
                return ETaskResult.StillRunning;
            }

            if (!_checkedOrTriggeredEquipmentUpdate)
            {
                _checkedOrTriggeredEquipmentUpdate = true;
                if (_gearsetterHadRecommendations)
                    gearsetModule->UpdateGearset(gearsetModule->CurrentGearsetIndex);
                _continueAt = DateTime.Now.AddSeconds(0.25);
                return ETaskResult.StillRunning;
            }

            return null;
        }

        private bool IsAllRecommendedGearEquipped()
        {
            RecommendEquipModule* recommendedEquipModule = RecommendEquipModule.Instance();
            InventoryManager* inventoryManager = InventoryManager.Instance();
            InventoryContainer* equippedItems =
                inventoryManager->GetInventoryContainer(InventoryType.EquippedItems);
            foreach (Pointer<InventoryItem> recommendedItemPtr in recommendedEquipModule->RecommendedItems)
            {
                InventoryItem* recommendedItem = recommendedItemPtr.Value;
                if (recommendedItem == null || recommendedItem->ItemId == 0)
                    continue;

                bool isEquipped = false;
                for (int i = 0; i < equippedItems->Size; ++i)
                {
                    InventoryItem equippedItem = equippedItems->Items[i];
                    if (equippedItem.ItemId != 0 && equippedItem.ItemId == recommendedItem->ItemId)
                    {
                        isEquipped = true;
                        break;
                    }
                }

                if (!isEquipped)
                    return false;
            }

            return true;
        }

        public override bool ShouldInterruptOnDamage() => true;
    }
}
