using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MegaCrit.Sts2.Core.Logging;

namespace AITeammate.Scripts;

internal static class EnemyReactiveMetadataReporter
{
    public static string WriteDebugReport(EnemyReactiveMetadataRepository repository)
    {
        try
        {
            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
            string reportPath = Path.Combine(assemblyDir, "enemy_reactive_metadata_report.txt");
            File.WriteAllText(reportPath, BuildReportText(repository));
            Log.Info($"[AITeammate] Wrote enemy reactive metadata report path={reportPath}");
            return reportPath;
        }
        catch (Exception ex)
        {
            Log.Error($"[AITeammate] Failed to write enemy reactive metadata report: {ex}");
            return string.Empty;
        }
    }

    private static string BuildReportText(EnemyReactiveMetadataRepository repository)
    {
        EnemyReactiveMetadataBuildReport report = repository.BuildReport;
        StringBuilder builder = new();
        builder.AppendLine("AITeammate Enemy Reactive Metadata Report");
        builder.AppendLine($"Generated: {DateTime.Now:O}");
        builder.AppendLine();
        builder.AppendLine("Feasibility");
        builder.AppendLine($"SourceRoot: {report.SourceRoot}");
        builder.AppendLine($"TotalPowerTypesScanned: {report.TotalPowerTypesScanned}");
        builder.AppendLine($"ReactivePowerCount: {report.ReactivePowerCount}");
        builder.AppendLine($"FullyStaticPowers: {report.FullyStaticPowers}");
        builder.AppendLine($"RuntimeResolvedPowers: {report.RuntimeResolvedPowers}");
        builder.AppendLine($"PartialPowers: {report.PartialPowers}");
        foreach (string finding in report.Findings)
        {
            builder.AppendLine($"- {finding}");
        }

        builder.AppendLine();
        builder.AppendLine("HighRiskReactivePowers");
        foreach (EnemyReactiveMetadata metadata in repository.All
                     .Where(static item => item.RetaliatesDamage || item.CardFlowPunishment || item.AddsStatusCards || item.AddsCurseCards || item.RestrictsPlay || item.ModifiesIncomingDamage)
                     .OrderBy(static item => item.PowerId, StringComparer.Ordinal))
        {
            builder.AppendLine($"- {metadata.PowerId} retaliates={metadata.RetaliatesDamage} cardFlow={metadata.CardFlowPunishment} addsStatus={metadata.AddsStatusCards} addsCurse={metadata.AddsCurseCards} restrictsPlay={metadata.RestrictsPlay} modifiesDamage={metadata.ModifiesIncomingDamage}");
        }

        foreach (EnemyReactiveMetadata metadata in repository.All.OrderBy(static item => item.PowerId, StringComparer.Ordinal))
        {
            builder.AppendLine();
            builder.AppendLine($"[{metadata.PowerId}] {metadata.DisplayName}");
            builder.AppendLine($"Type={metadata.SourceTypeName}");
            builder.AppendLine($"Source={metadata.SourceFilePath}");
            builder.AppendLine($"PowerType={metadata.PowerType}");
            builder.AppendLine($"Flags reactive={metadata.HasReactivePunishment} retaliates={metadata.RetaliatesDamage} cardFlow={metadata.CardFlowPunishment} debuffs={metadata.AppliesDebuffs} addsStatus={metadata.AddsStatusCards} addsCurse={metadata.AddsCurseCards} afflicts={metadata.AfflictsCards} changeStr={metadata.ChangesStrength} changeDex={metadata.ChangesDexterity} gainBlock={metadata.GrantsBlock} modifyDamage={metadata.ModifiesIncomingDamage} restrictsPlay={metadata.RestrictsPlay} modifyCosts={metadata.ModifiesCardCosts}");
            builder.AppendLine($"Retaliation blockable={metadata.IncludesBlockableRetaliation} unblockable={metadata.IncludesUnblockableRetaliation} requiresRuntime={metadata.RequiresRuntimeResolution} partial={metadata.HasPartialUnknowns}");
            if (metadata.DynamicVars.Count > 0)
            {
                builder.AppendLine($"DynamicVars={string.Join(", ", metadata.DynamicVars.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => $"{pair.Key}={pair.Value}"))}");
            }

            foreach (EnemyReactiveEffectDescriptor effect in metadata.Effects)
            {
                builder.AppendLine($"  effect {effect.Describe()} immediateHp={effect.ImmediateHpPunishment} cardFlow={effect.CardFlowPunishment} delayed={effect.DelayedPunishment} global={effect.GlobalPunishment} runtime={effect.RequiresRuntimeResolution} notes={effect.Notes}");
            }

            if (!string.IsNullOrWhiteSpace(metadata.Notes))
            {
                builder.AppendLine($"Notes={metadata.Notes}");
            }
        }

        return builder.ToString();
    }
}
