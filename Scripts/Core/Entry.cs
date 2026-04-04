using System.Linq;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace AITeammate.Scripts;

[ModInitializer("Init")]
public class Entry
{
    private const string ModId = "sts2.aiteammate";

    public static void Init()
    {
        Logger.SetLogLevelForType(LogType.Generic, LogLevel.Debug);

        var harmony = new Harmony(ModId);
        harmony.PatchAll();

        ScriptManagerBridge.LookupScriptsInAssembly(typeof(Entry).Assembly);

        PotionMetadataRepository potionRepository = PotionMetadataRepository.Shared;
        string reportPath = PotionMetadataReporter.WriteDebugReport(potionRepository);
        EnemyReactiveMetadataRepository reactiveRepository = EnemyReactiveMetadataRepository.Shared;
        string reactiveReportPath = EnemyReactiveMetadataReporter.WriteDebugReport(reactiveRepository);

        Log.Info("[AITeammate] Generic log level set to Debug.");
        Log.Info("[AITeammate] Init reached.");
        Log.Debug("[AITeammate] Debug log reached.");
        Log.Info($"[AITeammate] Potion metadata ready count={potionRepository.All.Count()} reportPath={reportPath}");
        Log.Info($"[AITeammate] Enemy reactive metadata ready count={reactiveRepository.All.Count()} reportPath={reactiveReportPath}");
    }
}
