using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MegaCrit.Sts2.Core.Logging;

namespace AITeammate.Scripts;

internal static class PotionMetadataReporter
{
    public static string WriteDebugReport(PotionMetadataRepository repository)
    {
        try
        {
            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
            string reportPath = Path.Combine(assemblyDir, "potion_metadata_report.txt");
            File.WriteAllText(reportPath, BuildReportText(repository));
            Log.Info($"[AITeammate] Wrote potion metadata report path={reportPath}");
            return reportPath;
        }
        catch (Exception ex)
        {
            Log.Error($"[AITeammate] Failed to write potion metadata report: {ex}");
            return string.Empty;
        }
    }

    private static string BuildReportText(PotionMetadataRepository repository)
    {
        PotionMetadataBuildReport report = repository.BuildReport;
        StringBuilder builder = new();
        builder.AppendLine("AITeammate Potion Metadata Report");
        builder.AppendLine($"Generated: {DateTime.Now:O}");
        builder.AppendLine();
        builder.AppendLine("Feasibility");
        builder.AppendLine($"SourceRoot: {report.SourceRoot}");
        builder.AppendLine($"TotalPotions: {report.TotalPotions}");
        builder.AppendLine($"FullyStaticPotions: {report.FullyStaticPotions}");
        builder.AppendLine($"RuntimeResolvedPotions: {report.RuntimeResolvedPotions}");
        builder.AppendLine($"PartialPotions: {report.PartialPotions}");
        foreach (string finding in report.Findings)
        {
            builder.AppendLine($"- {finding}");
        }

        builder.AppendLine();
        builder.AppendLine("HighRiskPotions");
        foreach (PotionMetadata metadata in repository.All.Where(static item => item.HarmsSelf || item.HarmsTeammate || item.HarmsAllAllies || item.MixedBenefitAndHarm).OrderBy(static item => item.PotionId, StringComparer.Ordinal))
        {
            builder.AppendLine($"- {metadata.PotionId} harmsSelf={metadata.HarmsSelf} harmsTeammate={metadata.HarmsTeammate} harmsAllAllies={metadata.HarmsAllAllies} mixed={metadata.MixedBenefitAndHarm}");
        }

        foreach (PotionMetadata metadata in repository.All.OrderBy(static item => item.PotionId, StringComparer.Ordinal))
        {
            builder.AppendLine();
            builder.AppendLine($"[{metadata.PotionId}] {metadata.DisplayName}");
            builder.AppendLine($"Type={metadata.SourceTypeName}");
            builder.AppendLine($"Source={metadata.SourceFilePath}");
            builder.AppendLine($"Usage={metadata.Usage} TargetType={metadata.DeclaredTargetType} TargetKinds={string.Join(", ", metadata.TargetKinds)}");
            builder.AppendLine($"Flags offensive={metadata.Offensive} defensive={metadata.Defensive} utility={metadata.Utility} harmsSelf={metadata.HarmsSelf} harmsTeammate={metadata.HarmsTeammate} harmsAllAllies={metadata.HarmsAllAllies} mixed={metadata.MixedBenefitAndHarm}");
            builder.AppendLine($"StaticMagnitudes={metadata.EffectMagnitudesStaticallyKnown} RequiresRuntime={metadata.RequiresRuntimeResolution} Partial={metadata.HasPartialUnknowns}");
            if (metadata.DynamicVars.Count > 0)
            {
                builder.AppendLine($"DynamicVars={string.Join(", ", metadata.DynamicVars.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => $"{pair.Key}={pair.Value}"))}");
            }

            foreach (PotionEffectDescriptor effect in metadata.Effects)
            {
                builder.AppendLine($"  effect {effect.Describe()} affectSelf={effect.CanAffectSelf} affectTeammate={effect.CanAffectTeammate} affectAllAllies={effect.CanAffectAllAllies} affectSingleEnemy={effect.CanAffectSingleEnemy} affectAllEnemies={effect.CanAffectAllEnemies} notes={effect.Notes}");
            }

            if (!string.IsNullOrWhiteSpace(metadata.Notes))
            {
                builder.AppendLine($"Notes={metadata.Notes}");
            }
        }

        return builder.ToString();
    }
}
