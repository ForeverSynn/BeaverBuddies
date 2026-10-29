using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Timberborn.Modding;
using Timberborn.SingletonSystem;

namespace BeaverBuddies.ModCompat
{
    [Serializable]
    public class ModInfo
    {
        public string id;
        public string name;
        public string version;

        public override string ToString()
        {
            return $"{name} ({version})";
        }
    }

    /**
     * Keeps track of which mods are enabled, so that the Host and Clients
     * can check they are running the same mods (a mismatch is a very likely
     * cause of desyncs), and logs information that helps diagnose conflicts
     * with other mods.
     */
    public class ModCompatibilityService : ILoadableSingleton
    {
        private static bool hasLoggedPatchConflicts = false;

        private readonly ModRepository _modRepository;

        /// <summary>
        /// The mods enabled by this player, sorted by id. Static because it
        /// is needed when creating the connection handshake, which happens
        /// outside of dependency injection.
        /// </summary>
        public static List<ModInfo> EnabledMods { get; private set; } = new List<ModInfo>();

        public ModCompatibilityService(ModRepository modRepository)
        {
            _modRepository = modRepository;
        }

        public void Load()
        {
            EnabledMods = _modRepository.EnabledMods
                .Select(mod => new ModInfo()
                {
                    id = mod.Manifest.Id,
                    name = mod.Manifest.Name,
                    version = mod.Manifest.Version.Full,
                })
                .OrderBy(mod => mod.id)
                .ToList();
            Plugin.Log($"Enabled mods ({EnabledMods.Count}): {string.Join(", ", EnabledMods)}");

            // All mods have applied their patches by the time a scene loads,
            // so this is a good moment to look for overlaps (and once is enough).
            if (!hasLoggedPatchConflicts)
            {
                hasLoggedPatchConflicts = true;
                LogPatchConflicts();
            }
        }

        /**
         * Returns a human-readable description of the differences between
         * the Host's mods and this player's mods, or null if they match.
         */
        public static string DescribeDifferences(IEnumerable<ModInfo> hostMods, IEnumerable<ModInfo> clientMods)
        {
            var host = ToDictionary(hostMods);
            var client = ToDictionary(clientMods);

            var onlyHost = host.Values.Where(mod => !client.ContainsKey(mod.id)).ToList();
            var onlyClient = client.Values.Where(mod => !host.ContainsKey(mod.id)).ToList();
            var differentVersions = host.Values
                .Where(mod => client.ContainsKey(mod.id) && client[mod.id].version != mod.version)
                .ToList();

            if (onlyHost.Count == 0 && onlyClient.Count == 0 && differentVersions.Count == 0)
            {
                return null;
            }

            StringBuilder message = new StringBuilder();
            message.AppendLine("Warning! The Host is not using the same mods as you, which is likely to cause desyncs. " +
                "All players should enable the same mods, with the same versions.");
            if (onlyHost.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Mods only the Host has enabled:");
                foreach (var mod in onlyHost) message.AppendLine($"- {mod}");
            }
            if (onlyClient.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Mods only you have enabled:");
                foreach (var mod in onlyClient) message.AppendLine($"- {mod}");
            }
            if (differentVersions.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("Mods with different versions:");
                foreach (var mod in differentVersions)
                {
                    message.AppendLine($"- {mod.name} (Host: {mod.version}, you: {client[mod.id].version})");
                }
            }
            return message.ToString().TrimEnd();
        }

        private static Dictionary<string, ModInfo> ToDictionary(IEnumerable<ModInfo> mods)
        {
            // Two mods can share an id (the game marks them as duplicated),
            // so don't assume ids are unique.
            return mods
                .Where(mod => mod?.id != null)
                .GroupBy(mod => mod.id)
                .ToDictionary(group => group.Key, group => group.First());
        }

        /**
         * Logs every game method that is patched both by BeaverBuddies and by
         * another mod. BeaverBuddies replaces the behavior of several methods
         * entirely (see ManualMethodOverwrite), so another mod patching the
         * same method is the most common source of mod incompatibilities.
         */
        private static void LogPatchConflicts()
        {
            try
            {
                foreach (var method in Harmony.GetAllPatchedMethods())
                {
                    var patches = Harmony.GetPatchInfo(method);
                    if (patches == null || !patches.Owners.Contains(Plugin.ID)) continue;
                    var others = patches.Owners.Where(owner => owner != Plugin.ID).Distinct().ToList();
                    if (others.Count == 0) continue;
                    Plugin.LogWarning($"Mod compatibility: {method.DeclaringType?.FullName}.{method.Name} is patched " +
                        $"by {Plugin.Name} and also by: {string.Join(", ", others)}. " +
                        $"If you experience desyncs, try disabling those mods.");
                }
            }
            catch (Exception e)
            {
                Plugin.LogWarning($"Failed to check for patch conflicts: {e}");
            }
        }
    }
}
