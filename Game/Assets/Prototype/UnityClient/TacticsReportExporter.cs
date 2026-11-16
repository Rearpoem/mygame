using System;
using System.Globalization;
using System.IO;
using System.Text;
using Prototype.Tactics;
using UnityEngine;

namespace Prototype.UnityClient
{
    internal static class TacticsReportExporter
    {
        internal static string Export(TacticsWorkbenchSession workbench, TacticsFormInput form,
            TacticsCatalogSnapshot catalog, string rootOverride)
        {
            var root = ResolveRoot(rootOverride);
            var directory = Path.Combine(root, "tactics_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)
                + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var accepted = workbench.GetAcceptance();
            Directory.CreateDirectory(directory);
            // Keep the actual service DTOs intact: new action, MP, multi-target and
            // state fields are serialized without a UI-owned field projection.
            WriteJson(directory, "session_result.json", accepted);
            WriteJson(directory, "loaded_scenario.json", accepted.loadedScenario);
            WriteJson(directory, "tactics_input.json", accepted.draftScenario);
            WriteJson(directory, "committed_scenario.json", accepted.committedScenario);
            WriteJson(directory, "instruction_form.json", form);
            WriteJson(directory, "catalog_snapshot.json", catalog);
            Write(directory, "tactics_report.txt", accepted.reportText);
            Write(directory, "battle_report.txt", accepted.reportText);
            WriteJson(directory, "export_manifest.json", new Manifest
            {
                schemaVersion = 1,
                exportedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                draftRevision = accepted.draftRevision,
                committedRevision = accepted.committedRevision,
                hasUncommittedChanges = accepted.hasUncommittedChanges,
                recordedEventCount = accepted.battleHistory == null ? 0 : accepted.battleHistory.Length,
                recordedComparison = accepted.lastComparison != null && accepted.lastComparison.success,
                outputDirectory = directory
            });
            return directory;
        }

        private static string ResolveRoot(string requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
            {
                var args = Environment.GetCommandLineArgs();
                for (var i = 0; i < args.Length; i++)
                    if (args[i] == "--prototype-report-dir")
                    {
                        if (i + 1 >= args.Length) throw new ArgumentException("--prototype-report-dir 后需要绝对目录。");
                        requested = args[i + 1];
                        break;
                    }
            }
            if (string.IsNullOrWhiteSpace(requested)) return Path.Combine(Application.persistentDataPath, "Reports");
            if (!Path.IsPathRooted(requested)) throw new ArgumentException("导出根目录必须为绝对路径。");
            var pathRoot = Path.GetPathRoot(requested);
            if (Path.DirectorySeparatorChar == '\\' && !(pathRoot.StartsWith("\\\\", StringComparison.Ordinal)
                || (pathRoot.Length >= 3 && pathRoot[1] == ':' && (pathRoot[2] == '\\' || pathRoot[2] == '/'))))
                throw new ArgumentException("导出目录必须包含盘符根目录或完整 UNC 路径。");
            return Path.GetFullPath(requested);
        }

        private static void WriteJson(string directory, string name, object value)
        {
            Write(directory, name, value == null ? "null" : JsonUtility.ToJson(value, true));
        }
        private static void Write(string directory, string name, string text)
        {
            File.WriteAllText(Path.Combine(directory, name), text ?? "", new UTF8Encoding(false));
        }
        [Serializable]
        private sealed class Manifest
        {
            public int schemaVersion;
            public string exportedAtUtc;
            public int draftRevision;
            public int committedRevision;
            public bool hasUncommittedChanges;
            public int recordedEventCount;
            public bool recordedComparison;
            public string outputDirectory;
        }
    }
}
