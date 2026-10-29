// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @blackappleD
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using Questionable.Controller.Steps.Common;
using Questionable.Functions;
using Questionable.Model.Questing;
namespace Questionable.Controller.Steps.Shared;

/// <summary>
/// Remembers the gear coffers a quest hands out when it is turned in, and opens them as part of the next batch of
/// tasks. The turn-in's own trailing tasks can't be used for this, as the quest controller discards them as soon as
/// the game moves on to the next quest.
/// </summary>
[RegisterSingleton]
internal sealed class QuestRewardCofferTracker(
    QuestData questData,
    QuestFunctions questFunctions,
    IDataManager dataManager,
    Configuration configuration,
    ILogger<QuestRewardCofferTracker> logger)
{
    private static readonly TimeSpan WaitForRewardTimeout = TimeSpan.FromSeconds(5);

    private readonly Dictionary<QuestId, List<ItemReward>> _pending = [];

    public void RegisterTurnIn(ElementId elementId)
    {
        if (!configuration.General.AutoOpenQuestRewardGearCoffers || elementId is not QuestId questId)
            return;

        if (_pending.ContainsKey(questId))
            return;

        if (!questData.TryGetQuestInfo(questId, out IQuestInfo? questInfo) || questInfo is not QuestInfo info)
            return;

        ExcelSheet<Item> itemSheet = dataManager.GetExcelSheet<Item>();
        List<ItemReward> coffers = info.ItemRewards.Select(x => x.ItemId)
            .Concat(info.ItemRewardsRaw)
            .Distinct()
            .Select(itemId => itemSheet.GetRowOrDefault(itemId))
            .Where(item => item != null && ItemReward.IsGearCoffer(item.Value))
            .Select(item => ItemReward.CreateFromItem(item!.Value, questId))
            .OfType<ItemReward>()
            .ToList();
        if (coffers.Count == 0)
            return;

        logger.LogInformation("Quest {QuestId} rewards gear coffers: {Coffers}", questId, string.Join(", ", coffers.Select(x => x.Name)));
        _pending[questId] = coffers;
    }

    /// <summary>
    /// Returns the tasks to open the coffers of quests that have been turned in since the last call.
    /// </summary>
    public List<ITask> TakeTasks()
    {
        if (_pending.Count == 0)
            return [];

        if (!configuration.General.AutoOpenQuestRewardGearCoffers)
        {
            _pending.Clear();
            return [];
        }

        List<ItemReward> coffers = [];
        foreach (QuestId questId in _pending.Keys.ToList())
        {
            // the turn-in might not have happened yet, e.g. if it got interrupted
            if (!questFunctions.IsQuestComplete(questId))
                continue;

            coffers.AddRange(_pending[questId]);
            _pending.Remove(questId);
        }

        if (coffers.Count == 0)
            return [];

        // rewards can take a moment to show up in the inventory after the turn-in
        DateTime? waitUntil = null;
        uint[] itemIds = coffers.Select(x => x.ItemId).ToArray();
        WaitCondition.Task waitForReward = new(() =>
        {
            waitUntil ??= DateTime.Now.Add(WaitForRewardTimeout);
            return DateTime.Now > waitUntil || HasAnyItem(itemIds);
        }, "Wait(quest reward coffer)");

        return
        [
            waitForReward,
            new MountStep.UnmountTask(),
            .. coffers.Select(ITask (x) => new RedeemRewardItems.Task(x))
        ];
    }

    private static unsafe bool HasAnyItem(uint[] itemIds)
    {
        InventoryManager* inventoryManager = InventoryManager.Instance();
        return inventoryManager != null && itemIds.Any(x => inventoryManager->GetInventoryItemCount(x) > 0);
    }
}
