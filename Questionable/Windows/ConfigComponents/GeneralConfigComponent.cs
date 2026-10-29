using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ECommons.ExcelServices;
using ECommons.ImGuiMethods;
using Lumina.Excel.Sheets;
using Questionable.Model.Common;
using Questionable.Model.Questing;
using Questionable.Windows.Common.Ui;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;
namespace Questionable.Windows.ConfigComponents;

[RegisterSingleton]
internal sealed class GeneralConfigComponent : ConfigComponent
{
    private static string[] GrandCompanyNames =>
    [
        _L("Auto"), _L("Maelstrom"), _L("Twin Adder"), _L("Immortal Flames")
    ];

    private readonly QuestRegistry _questRegistry;
    private readonly TerritoryData _territoryData;
    private readonly Lazy<List<Job>> _sortedClassJobs;
    private readonly Lazy<(uint[] Ids, string[] Names)> _mounts;
    private readonly Lazy<(Job[] Ids, string[] Names)> _classJobs;
    private readonly Lazy<(Job[] Ids, string[] Names)> _craftJobs;
    private readonly Lazy<(Job[] Ids, string[] Names)> _gatherJobs;
    private string _mountSearchString = string.Empty;
    private string _langSearchString = string.Empty;

    public GeneralConfigComponent(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        IDataManager dataManager,
        ClassJobUtils classJobUtils,
        QuestRegistry questRegistry,
        TerritoryData territoryData)
        : base(pluginInterface, configuration)
    {
        _questRegistry = questRegistry;
        _territoryData = territoryData;

        _sortedClassJobs = new(() => [.. classJobUtils.SortedClassJobs.Select(x => x.ClassJob)]);
        _mounts = new(() => BuildMounts(dataManager));
        _classJobs = new(() => BuildJobList(
            Enum.GetValues<Job>().Where(x => x != Job.ADV && !x.IsCrafter() && !x.IsGatherer() && !x.IsClass()),
            prependDefault: true));
        _craftJobs = new(() => BuildJobList(
            Enum.GetValues<Job>().Where(x => x != Job.ADV && x.IsCrafter()),
            prependDefault: false));
        _gatherJobs = new(() => BuildJobList(
            Enum.GetValues<Job>().Where(x => x == Job.MIN || x == Job.BTN),
            prependDefault: false));
    }

    private static (uint[] Ids, string[] Names) BuildMounts(IDataManager dataManager)
    {
        List<(uint MountId, string Name)> mounts = dataManager.GetExcelSheet<Mount>()
            .Where(x => x is { RowId: > 0, Icon: > 0 })
            .Select(x => (MountId: x.RowId, Name: x.Singular.ToString()))
            .Where(x => !string.IsNullOrEmpty(x.Name))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToList();
        uint[] ids = [0, .. mounts.Select(x => x.MountId)];
        string[] names = [_L("Mount Roulette"), .. mounts.Select(x => x.Name)];
        return (ids, names);
    }

    private (Job[] Ids, string[] Names) BuildJobList(IEnumerable<Job> source, bool prependDefault)
    {
        List<Job> sorted = _sortedClassJobs.Value;
        List<Job> jobs = [.. source.OrderBy(x => sorted.IndexOf(x))];
        if (prependDefault)
        {
            Job[] ids = [Job.ADV, .. jobs];
            string[] names = [_L("Auto (highest level/item level)"), .. jobs.Select(x => x.ToFriendlyString())];
            return (ids, names);
        }
        return ([.. jobs], [.. jobs.Select(x => x.ToFriendlyString())]);
    }

    public override void DrawTab()
    {
        using ImRaii.TabItemDisposable tab = ImRaii.TabItem(_L("General") + "###General");
        if (!tab)
            return;
        var size = ImGui.GetWindowContentRegionMax();
        using var _ = ImRaii.TextWrapPos(size.X);
        Dictionary<string, string> languages = new(StringComparer.Ordinal){
            { "en",    _L("English") },
            { "ja-jp", _L("Japanese") },
            { "zh-cn", _L("Chinese (Simplified)") },
            { "zh-tw", _L("Chinese (Traditional)") },
            { "ko",    _L("Korean") + " (WIP)" }
        };
        string language = Configuration.General.Language;
        if (ImGuiComponentsLocal.DrawSearchableCombo(_L("Language"), languages.Keys.ToArray(), languages.Values.ToArray(),
            Configuration.General.Language, ref _langSearchString, ref language))
        {
            var was = Configuration.General.Language;
            Configuration.General.Language = language;
            Save();
            if (was != Configuration.General.Language)
                DalamudInitializer.SetupI18N(Configuration.General.Language);
        }

        if (QstWidgets.SectionHeader(_L("Preferences"), "Preferences", defaultOpen: false))
        {
            ECombatModule combatModule = Configuration.General.CombatModule;
            ImGui.SetNextItemWidth(size.X / 2);
            if (ImGuiEx.EnumCombo(_L("Preferred Combat Module"), ref combatModule))
            {
                Configuration.General.CombatModule = combatModule;
                Save();
            }

            (uint[] mountIds, string[] mountNames) = _mounts.Value;
            uint mountId = Configuration.General.MountId;
            ImGui.SetNextItemWidth(size.X / 2);
            if (ImGuiComponentsLocal.DrawSearchableCombo(_L("Preferred Mount"), mountIds, mountNames,
                Configuration.General.MountId, ref _mountSearchString, ref mountId))
            {
                Configuration.General.MountId = mountId;
                Save();
            }

            int grandCompany = (int)Configuration.General.GrandCompany;
            ImGui.SetNextItemWidth(size.X / 2);
            if (ImGui.Combo(_L("Preferred Grand Company"), ref grandCompany, GrandCompanyNames,
                GrandCompanyNames.Length))
            {
                Configuration.General.GrandCompany = (GrandCompany)grandCompany;
                Save();
            }

            (Job[] classJobIds, string[] classJobNames) = _classJobs.Value;
            ImGui.SetNextItemWidth(size.X / 2);
            DrawComboOption(_L("Preferred Combat Job"), classJobIds, classJobNames,
                () => Configuration.General.CombatJob,
                v => Configuration.General.CombatJob = v);

            (Job[] craftJobIds, string[] craftJobNames) = _craftJobs.Value;
            ImGui.SetNextItemWidth(size.X / 2);
            DrawComboOption(_L("Preferred Crafting Job"), craftJobIds, craftJobNames,
                () => Configuration.General.CraftingJob,
                v => Configuration.General.CraftingJob = v);

            (Job[] gatherJobIds, string[] gatherJobNames) = _gatherJobs.Value;
            ImGui.SetNextItemWidth(size.X / 2);
            DrawComboOption(_L("Preferred Gathering Job"), gatherJobIds, gatherJobNames,
                () => Configuration.General.GatheringJob,
                v => Configuration.General.GatheringJob = v);

            ImGui.BeginGroup();
            using (ImRaii.Disabled(!StylistIpc.IsInstalled && !GearsetterIpc.IsInstalled))
            {
                EGearsetUpdateSource gearsetSource = Configuration.General.GearsetUpdateSource;
                ImGui.SetNextItemWidth(size.X / 2);
                if (ImGuiEx.EnumCombo(_L("Preferred Gear Upgrade Source"), ref gearsetSource))
                {
                    Configuration.General.GearsetUpdateSource = gearsetSource;
                    Save();
                }
                if (!StylistIpc.IsInstalled && gearsetSource is EGearsetUpdateSource.Stylist)
                {
                    Svc.Chat.Print(_L("You've set Stylist to manage equipped gear, but it is not installed. Resetting to Vanilla."), CommandHandler.MessageTag, CommandHandler.TagColor);
                    Configuration.General.GearsetUpdateSource = EGearsetUpdateSource.Vanilla;
                    Save();
                }
                if (!GearsetterIpc.IsInstalled && gearsetSource is EGearsetUpdateSource.Gearsetter)
                {
                    Svc.Chat.Print(_L("You've set Gearsetter to manage equipped gear, but it is not installed. Resetting to Vanilla."), CommandHandler.MessageTag, CommandHandler.TagColor);
                    Configuration.General.GearsetUpdateSource = EGearsetUpdateSource.Vanilla;
                    Save();
                }
            }
            ImGui.EndGroup();
            if (!StylistIpc.IsInstalled && !GearsetterIpc.IsInstalled && ImGui.IsItemHovered())
                ImGui.SetTooltip(_L("Neither Stylist nor Gearsetter is installed."));

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextDisabled(FontAwesomeIcon.InfoCircle.ToIconString());
            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.Text(_L("These settings take effect after the plugin is reloaded."));
                }
            }

            string chocoboName = Configuration.General.ChocoboName;
            ImGui.SetNextItemWidth(size.X / 2);
            var genName = NameGenerator.GenerateFirstName(fast: grandCompany == 0);
            if (ImGui.InputTextEx(_L("Chocobo name"), genName, ref chocoboName, 20))
                Configuration.General.ChocoboName = chocoboName;

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                if (string.IsNullOrWhiteSpace(Configuration.General.ChocoboName))
                    Configuration.General.ChocoboName = genName;
                Save();
            }

            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.Text(_L("The name to give your chocobo during the \"My Little Chocobo\" quest."));
                    ImGui.Text(_L("Generates a random name if invalid or left blank."));
                }
            }

            string displayName = Configuration.General.DisplayName;
            ImGui.SetNextItemWidth(size.X / 2);
            if (ImGui.InputText(_L("Display name"), ref displayName, 20))
                Configuration.General.DisplayName = displayName;

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                if (string.IsNullOrWhiteSpace(Configuration.General.DisplayName))
                    Configuration.General.DisplayName = _L("Anonymous");
                Save();
            }

            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.Text(_L("The name associated with submissions to help with QST's development."));
                    ImGui.Text(_L("Defaults to \"Anonymous\" if left blank."));
                }
            }
        }

        if (QstWidgets.SectionHeader(_L("UI"), "UI", defaultOpen: false))
        {
            using (ImRaii.PushIndent())
            {
                bool useQuestionableTheme = Configuration.General.UseQuestionableTheme;
                if (ImGui.Checkbox(_L("Use Questionable theme"), ref useQuestionableTheme))
                {
                    Configuration.General.UseQuestionableTheme = useQuestionableTheme;
                    Save();
                }

                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.Text(_L("When disabled, Questionable's windows use your Dalamud style/theme instead."));
                    }
                }

                bool useEscToCancelQuesting = Configuration.General.UseEscToCancelQuesting;
                if (ImGui.Checkbox(_L("Use ESC to cancel questing/movement"), ref useEscToCancelQuesting))
                {
                    Configuration.General.UseEscToCancelQuesting = useEscToCancelQuesting;
                    Save();
                }

                bool showIncompleteSeasonalEvents = Configuration.General.ShowIncompleteSeasonalEvents;
                if (ImGui.Checkbox(_L("Show details for incomplete seasonal events"), ref showIncompleteSeasonalEvents))
                {
                    Configuration.General.ShowIncompleteSeasonalEvents = showIncompleteSeasonalEvents;
                    Save();
                }

                using (ImRaii.Disabled(!showIncompleteSeasonalEvents))
                {
                    bool showCompleteSeasonalEvents = Configuration.General.ShowCompleteSeasonalEvents;
                    if (ImGui.Checkbox(_L("Continue showing completed seasonal events"), ref showCompleteSeasonalEvents))
                    {
                        Configuration.General.ShowCompleteSeasonalEvents = showCompleteSeasonalEvents;
                        Save();
                    }
                }

                bool questIcons = Configuration.General.QuestIcons;
                if (ImGui.Checkbox(_L("Show quest icons"), ref questIcons))
                {
                    Configuration.General.QuestIcons = questIcons;
                    Save();
                }

                bool hideInAllInstances = Configuration.General.HideInAllInstances;
                if (ImGui.Checkbox(_L("Hide quest window in all instanced duties"), ref hideInAllInstances))
                {
                    Configuration.General.HideInAllInstances = hideInAllInstances;
                    Save();
                }

                bool hideSponsorButton = Configuration.General.HideSponsorButton;
                if (ImGui.Checkbox(_L("Hide Sponsor button"), ref hideSponsorButton))
                {
                    Configuration.General.HideSponsorButton = hideSponsorButton;
                    Save();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_L("Takes effect next time the plugin is loaded"));

                bool hideRemainingTasks = Configuration.General.HideRemainingTasks;
                if (ImGui.Checkbox(_L("Hide remaining tasks"), ref hideRemainingTasks))
                {
                    Configuration.General.HideRemainingTasks = hideRemainingTasks;
                    Save();
                }

                bool hidePatch = Configuration.General.HidePatch;
                if (ImGui.Checkbox(_L("Hide quest patch badge"), ref hidePatch))
                {
                    Configuration.General.HidePatch = hidePatch;
                    Save();
                }

                bool hideLevel = Configuration.General.HideQuestRequiredLevel;
                if (ImGui.Checkbox(_L("Hide quest level badge"), ref hideLevel))
                {
                    Configuration.General.HideQuestRequiredLevel = hideLevel;
                    Save();
                }

                bool hideQuestStartedJob = Configuration.General.HideQuestStartedJob;
                if (ImGui.Checkbox(_L("Hide which job a quest was started with"), ref hideQuestStartedJob))
                {
                    Configuration.General.HideQuestStartedJob = hideQuestStartedJob;
                    Save();
                }

                bool hideThingsNotAvailable = Configuration.General.HideThingsNotAvailable;
                if (ImGui.Checkbox(_L("Hide things unavailable in your account's unlocked expansions"), ref hideThingsNotAvailable))
                {
                    Configuration.General.HideThingsNotAvailable = hideThingsNotAvailable;
                    Save();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_L("If you are on free trial, or do not own the current expansion, this will hide content in locked expansions"));
            }
        }

#if REPORTING
        ImGui.Separator();
        ImGui.Text(_L("Bug Report"));
        using (ImRaii.PushIndent())
        {
            bool reportOptOut = Configuration.General.ReportsDisabled;
            if (ImGui.Checkbox(_L("Opt out of bug reports"), ref reportOptOut))
            {
                Configuration.General.ReportsDisabled = reportOptOut;
                Configuration.General.DismissedReportWarning = true;
                Save();
            }

            bool dismissedReportWarning = Configuration.General.DismissedReportWarning;
            if (ImGui.Checkbox(_L("Hide Report warning"), ref dismissedReportWarning))
            {
                Configuration.General.DismissedReportWarning = dismissedReportWarning;
                Save();
            }

            if (!reportOptOut)
            {
                string reportMessage = Configuration.General.ReportMessage;
                if (ImGui.InputText(_L("Report message"), ref reportMessage, 256))
                {
                    Configuration.General.ReportMessage = reportMessage;
                    Save();
                }
            }
        }
#endif

        if (QstWidgets.SectionHeader(_L("Questing"), "Questing", defaultOpen: false))
        {
            using (ImRaii.PushIndent())
            {
                bool configureTextAdvance = Configuration.General.ConfigureTextAdvance;
                if (ImGui.Checkbox(_L("Automatically configure TextAdvance with the recommended settings"),
                    ref configureTextAdvance))
                {
                    Configuration.General.ConfigureTextAdvance = configureTextAdvance;
                    Save();
                }

                if (configureTextAdvance)
                {
                    bool dontSkipCutscenes = Configuration.General.DontSkipCutscenes;
                    using (ImRaii.PushIndent())
                    {
                        if (ImGui.Checkbox(_L("but don't skip cutscenes or dialogue"), ref dontSkipCutscenes))
                        {
                            Configuration.General.DontSkipCutscenes = dontSkipCutscenes;
                            Save();
                        }
                    }
                }
                bool dontShowAnswerSuggestions = Configuration.General.DontShowAnswerSuggestions;
                if (ImGui.Checkbox(_L("Hide dialogue answer suggestions/notifications"), ref dontShowAnswerSuggestions))
                {
                    Configuration.General.DontShowAnswerSuggestions = dontShowAnswerSuggestions;
                    Save();
                }

                bool skipLowPriorityInstances = Configuration.General.SkipLowPriorityDuties;
                if (ImGui.Checkbox(_L("Unlock certain optional dungeons and raids (instead of waiting for completion)"), ref skipLowPriorityInstances))
                {
                    Configuration.General.SkipLowPriorityDuties = skipLowPriorityInstances;
                    Save();
                }

                ImGui.SameLine();
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    ImGui.TextDisabled(FontAwesomeIcon.InfoCircle.ToIconString());
                }

                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.Text(_L("Questionable automatically picks up some optional quests (e.g. for aether currents, or the ARR alliance raids)."));
                        ImGui.Text(_L("If this setting is enabled, Questionable will continue with other quests, instead of waiting for manual completion of the duty."));

                        ImGui.Separator();
                        ImGui.Text(_L("This affects the following dungeons and raids:"));
                        foreach ((uint ContentFinderConditionId, ElementId QuestId, int Sequence) lowPriorityCfc in _questRegistry.LowPriorityContentFinderConditionQuests)
                        {
                            if (_territoryData.TryGetContentFinderCondition(lowPriorityCfc.ContentFinderConditionId, out TerritoryData.ContentFinderConditionData? cfcData))
                                ImGui.BulletText($"{cfcData.Name}");
                        }
                    }
                }

                bool useTickets = Configuration.General.UseTickets;
                if (ImGui.Checkbox(_L("Use aetheryte tickets where available"), ref useTickets))
                {
                    Configuration.General.UseTickets = useTickets;
                    Save();
                }

                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.Text(_L("Ideally this should be set in the in-game Teleport settings, but is provided here for convenience."));
                    }
                }

                bool sameJobThroughoutQuest = Configuration.General.SameJobThroughoutQuest;
                if (ImGui.Checkbox(_L("Before each Interact, switch to the job a quest was accepted with"), ref sameJobThroughoutQuest))
                {
                    Configuration.General.SameJobThroughoutQuest = sameJobThroughoutQuest;
                    Save();
                }

                bool ignoreNextQuestIfPrioSet = Configuration.General.IgnoreNextQuestIfPrioSet;
                if (ImGui.Checkbox(_L("Ignore 'next quest' if Priority Quests has valid alternative"), ref ignoreNextQuestIfPrioSet))
                {
                    Configuration.General.IgnoreNextQuestIfPrioSet = ignoreNextQuestIfPrioSet;
                    Save();
                }

                bool autoOpenQuestRewardGearCoffers = Configuration.General.AutoOpenQuestRewardGearCoffers;
                if (ImGui.Checkbox(_L("Open gear coffers received as quest rewards"), ref autoOpenQuestRewardGearCoffers))
                {
                    Configuration.General.AutoOpenQuestRewardGearCoffers = autoOpenQuestRewardGearCoffers;
                    Save();
                }

                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.Text(_L("After turning in a quest, gear coffers that were part of that quest's rewards are opened automatically. Other usable items are not touched."));
                    }
                }

                //bool claimMail = Configuration.General.ClaimMail;
                //if (ImGui.Checkbox(_L("Claim mail"), ref claimMail))
                //{
                //    Configuration.General.ClaimMail = claimMail;
                //    Save();
                //}

                //if (ImGui.IsItemHovered())
                //{
                //    using (ImRaii.Tooltip())
                //    {
                //        ImGui.Text(_L("Use MogMail to claim all letters from Delivery Moogles when accepting a quest"));
                //    }
                //}

#if false
            ImGui.Spacing();
            bool autoStepRefreshEnabled = Configuration.General.AutoStepRefreshEnabled;
            if (ImGui.Checkbox(_L("Automatically refresh quest steps when stuck (WIP see tooltip)"), ref autoStepRefreshEnabled))
            {
                Configuration.General.AutoStepRefreshEnabled = autoStepRefreshEnabled;
                Save();
            }

            ImGui.SameLine();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextDisabled(FontAwesomeIcon.InfoCircle.ToIconString());
            }

            if (ImGui.IsItemHovered())
            {
                using (ImRaii.Tooltip())
                {
                    ImGui.Text(_L("Questionable will automatically refresh a quest step if it appears to be stuck after the configured delay."));
                    ImGui.Text(_L("This helps resume automated quest completion when interruptions occur."));
                    ImGui.Text(_L("WIP feature, rather than remove it, this is a warning that it isn't fully complete."));
                }
            }

            using (ImRaii.Disabled(!autoStepRefreshEnabled))
            {
                ImGui.Indent();
                int autoStepRefreshDelay = Configuration.General.AutoStepRefreshDelaySeconds;
                ImGui.SetNextItemWidth(150f);
                if (ImGui.SliderInt(_L("Refresh delay (seconds)"), ref autoStepRefreshDelay, 30, 180))
                {
                    Configuration.General.AutoStepRefreshDelaySeconds = autoStepRefreshDelay;
                    Save();
                }

                ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                    _LF("Quest steps will refresh automatically after {0} seconds if no progress is made.", autoStepRefreshDelay));
                ImGui.Unindent();
            }
#endif
            }
        }

        if (QstWidgets.SectionHeader(_L("Movement"), "Movement", defaultOpen: false))
        {
            using (ImRaii.PushIndent())
            {
                ImGui.Text(_L("When stuck during navmesh movement:"));
                ImGui.SameLine();
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    ImGui.TextDisabled(FontAwesomeIcon.InfoCircle.ToIconString());
                }

                if (ImGui.IsItemHovered())
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.Text(_L("If you make no progress towards the next waypoint for the configured delay:"));
                        ImGui.Text(_L("- Jump: attempts to jump over the obstacle"));
                        ImGui.Text(_L("- Recalculate path: stops and pathfinds to the destination again"));
                        ImGui.Text(_L("With both enabled, Questionable mostly recalculates the path and jumps every few attempts."));
                    }
                }

                using (ImRaii.PushIndent())
                {
                    bool jumpWhenStuck = Configuration.General.JumpWhenStuck;
                    if (ImGui.Checkbox(_L("Jump"), ref jumpWhenStuck))
                    {
                        Configuration.General.JumpWhenStuck = jumpWhenStuck;
                        Save();
                    }

                    ImGui.SameLine();
                    bool recalculatePathWhenStuck = Configuration.General.RecalculatePathWhenStuck;
                    if (ImGui.Checkbox(_L("Recalculate path"), ref recalculatePathWhenStuck))
                    {
                        Configuration.General.RecalculatePathWhenStuck = recalculatePathWhenStuck;
                        Save();
                    }

                    using (ImRaii.Disabled(!jumpWhenStuck && !recalculatePathWhenStuck))
                    {
                        int stuckDelayMs = Configuration.General.StuckDelayMs;
                        ImGui.SetNextItemWidth(150f);
                        if (ImGui.SliderInt(_L("Stuck delay (ms)"), ref stuckDelayMs, 500, 3000))
                        {
                            Configuration.General.StuckDelayMs = stuckDelayMs;
                            Save();
                        }
                    }
                }
            }
        }
    }
}
