using BeaverBuddies.Events;
using HarmonyLib;
using System;
using System.Linq;
using Timberborn.BeaversUI;
using Timberborn.BotsUI;
using Timberborn.Brushes;
using Timberborn.CursorToolSystem;
using Timberborn.Debugging;
using Timberborn.MortalSystem;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using Timberborn.WaterBrushesUI;
using Timberborn.WeatherSystem;
using UnityEngine;
using static BeaverBuddies.SingletonManager;

namespace BeaverBuddies.DevMode
{
    /**
     * Decides how a button in the game's Dev Panel (developer mode) is
     * handled in co-op. By default a dev method is treated like any other
     * player action: it is recorded and replayed on all players, so that
     * e.g. "Add 1000 Science" affects everyone and nobody desyncs. Mods that
     * add their own dev methods get this behavior for free.
     */
    static class DevMethodSyncPolicy
    {
        // Dev methods that only affect the local player's view, logging or
        // debugging tools. They run locally and are never shared.
        private static readonly string[] LocalOnlyPrefixes =
        {
            "Achievements:",
            "Auto move levels",
            "Automation: Log",
            "Camera",
            "Dump mesh metrics",
            "Highlight resource reproduction spots",
            "Load empty scene",
            "Multithreading:",
            "Reset debugging panels",
            "Save ",                            // "Save 10x to memory"
            "Sky:",
            "Stopwatch:",
            "Test ",                            // "Test exception", "Test warning", ...
            "Toggle GC",
            "Toggle deconstruction tool preview",
            "Toggle models:",
            "Toggle nav mesh",
            "Toggle sound listener debugger",
            "Toggle status slots",
            "Toggle water logic:",              // rendering only
            "Toggle zipline cable blocks",      // debug markers only
            "Trigger GC",
            "UI Scale:",
        };

        // Dev methods whose underlying game method is patched separately
        // (see below), because recording the button press alone would lose
        // information (e.g. which character is selected) or conflict with
        // an existing patch (game speed).
        private static readonly string[] PassThroughPrefixes =
        {
            "Jump to next ",
            "Kill all characters except selected",
            "Kill selected character",
            "Speed:",
        };

        public static bool IsLocalOnly(string name)
        {
            return StartsWithAny(name, LocalOnlyPrefixes);
        }

        public static bool IsPassThrough(string name)
        {
            return StartsWithAny(name, PassThroughPrefixes);
        }

        private static bool StartsWithAny(string name, string[] prefixes)
        {
            if (name == null) return false;
            return prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Serializable]
    class DevMethodInvokedEvent : ReplayEvent
    {
        public string methodName;

        public override void Replay(IReplayContext context)
        {
            var devPanel = GetSingleton<DevModeService>()?.DevPanel;
            var devMethod = devPanel?._devMethods?.FirstOrDefault(method => method.Name == methodName);
            if (devMethod == null)
            {
                Plugin.LogWarning($"Could not find dev method to replay: {methodName}");
                return;
            }
            devMethod.Invoke();
        }

        public override string ToActionString()
        {
            return $"Invoking dev method: {methodName}";
        }
    }

    [HarmonyPatch(typeof(DevMethod), nameof(DevMethod.Invoke))]
    class DevMethodInvokePatcher
    {
        static bool Prefix(DevMethod __instance)
        {
            string name = __instance.Name;
            if (DevMethodSyncPolicy.IsLocalOnly(name) || DevMethodSyncPolicy.IsPassThrough(name))
            {
                return true;
            }
            return ReplayEvent.DoPrefix(() => new DevMethodInvokedEvent()
            {
                methodName = name,
            });
        }
    }

    // ---- Time and weather fast-forwarding ----
    // These have their own key bindings, which bypass the Dev Panel, so the
    // underlying methods are patched rather than the panel buttons.

    [Serializable]
    class JumpToNextDaytimeEvent : ReplayEvent
    {
        public override void Replay(IReplayContext context)
        {
            GetSingleton<DevModeService>()?.TimeFastForwarder?.JumpToNextDaytime();
        }
    }

    [HarmonyPatch(typeof(TimeFastForwarder), nameof(TimeFastForwarder.JumpToNextDaytime))]
    class TimeFastForwarderJumpToNextDaytimePatcher
    {
        static bool Prefix()
        {
            return ReplayEvent.DoPrefix(() => new JumpToNextDaytimeEvent());
        }
    }

    [Serializable]
    class JumpToNextSeasonEvent : ReplayEvent
    {
        public override void Replay(IReplayContext context)
        {
            GetSingleton<DevModeService>()?.WeatherFastForwarder?.JumpToNextSeason();
        }
    }

    [HarmonyPatch(typeof(WeatherFastForwarder), nameof(WeatherFastForwarder.JumpToNextSeason))]
    class WeatherFastForwarderJumpToNextSeasonPatcher
    {
        static bool Prefix()
        {
            return ReplayEvent.DoPrefix(() => new JumpToNextSeasonEvent());
        }
    }

    // ---- "Tick once" (dev key binding) ----

    [Serializable]
    class TickOnceEvent : ReplayEvent
    {
        public override void Replay(IReplayContext context)
        {
            context.GetSingleton<ReplayService>().RequestSingleTick();
        }
    }

    [HarmonyPatch(typeof(Ticker), nameof(Ticker.TickOnce))]
    class TickerTickOncePatcher
    {
        static bool Prefix()
        {
            // Ticking outside of the normal update loop would immediately
            // desync, so instead all players advance exactly one tick.
            return ReplayEvent.DoPrefix(() => new TickOnceEvent());
        }
    }

    // ---- Killing characters ----

    [Serializable]
    class KillCharacterEvent : ReplayEvent
    {
        public string entityID;
        public bool dieInstantly;

        public override void Replay(IReplayContext context)
        {
            Mortal mortal = GetComponent<Mortal>(context, entityID);
            if (mortal == null || mortal.ShouldDie) return;
            CharacterKiller.KillCharacter(mortal, dieInstantly);
        }

        public override string ToActionString()
        {
            return $"Killing character {entityID} (instantly: {dieInstantly})";
        }
    }

    // Covers the "Kill selected character" button and its key binding, as
    // well as the kill-all/kill-some buttons when they are replayed.
    [HarmonyPatch(typeof(CharacterKiller), nameof(CharacterKiller.KillCharacter))]
    class CharacterKillerKillCharacterPatcher
    {
        static bool Prefix(Mortal mortal, bool dieInstantly)
        {
            return ReplayEvent.DoEntityPrefix(mortal, entityID => new KillCharacterEvent()
            {
                entityID = entityID,
                dieInstantly = dieInstantly,
            });
        }
    }

    [Serializable]
    class KillAllExceptSelectedEvent : ReplayEvent
    {
        public string exceptEntityID;

        public override void Replay(IReplayContext context)
        {
            CharacterKiller killer = GetSingleton<DevModeService>()?.CharacterKiller;
            if (killer == null) return;
            Mortal except = exceptEntityID == null ? null : GetComponent<Mortal>(context, exceptEntityID);

            // The game uses whichever character is selected locally, which
            // differs between players, so temporarily use the recorded one.
            Mortal previouslySelected = killer._selectedMortal;
            killer._selectedMortal = except;
            try
            {
                killer.KillAllExceptSelected();
            }
            finally
            {
                killer._selectedMortal = previouslySelected;
            }
        }

        public override string ToActionString()
        {
            return $"Killing all characters except {exceptEntityID}";
        }
    }

    [HarmonyPatch(typeof(CharacterKiller), nameof(CharacterKiller.KillAllExceptSelected))]
    class CharacterKillerKillAllExceptSelectedPatcher
    {
        static bool Prefix(CharacterKiller __instance)
        {
            return ReplayEvent.DoPrefix(() => new KillAllExceptSelectedEvent()
            {
                // The selected character may have died since it was selected
                exceptEntityID = __instance._selectedMortal ? ReplayEvent.GetEntityID(__instance._selectedMortal) : null,
            });
        }
    }

    // ---- Dev tools that spawn things at the cursor ----

    /**
     * Lets replayed events run the game's own cursor-based tools (e.g. the
     * dev-mode beaver spawner) at the position the original player clicked,
     * rather than wherever the replaying player's mouse happens to be.
     */
    [HarmonyPatch(typeof(CursorCoordinatesPicker), nameof(CursorCoordinatesPicker.Pick))]
    static class CursorCoordinatesPickerPatcher
    {
        private static CursorCoordinates? overrideCoordinates = null;

        public static void WithCursorAt(Vector3 coordinates, Vector3Int tileCoordinates, Action action)
        {
            overrideCoordinates = new CursorCoordinates(coordinates, tileCoordinates);
            try
            {
                action();
            }
            finally
            {
                overrideCoordinates = null;
            }
        }

        static bool Prefix(ref CursorCoordinates? __result)
        {
            if (!overrideCoordinates.HasValue) return true;
            __result = overrideCoordinates;
            return false;
        }
    }

    [Serializable]
    class SpawnBeaversEvent : ReplayEvent
    {
        public Vector3 coordinates;
        public Vector3Int tileCoordinates;
        public bool isChild;
        public int count;

        public override void Replay(IReplayContext context)
        {
            BeaverGeneratorTool tool = GetSingleton<DevModeService>()?.BeaverGeneratorTool;
            if (tool == null) return;
            CursorCoordinatesPickerPatcher.WithCursorAt(coordinates, tileCoordinates,
                () => tool.PlaceBeavers(isChild, count));
        }

        public override string ToActionString()
        {
            return $"Spawning {count} beaver(s) (child: {isChild}) at {tileCoordinates}";
        }
    }

    [HarmonyPatch(typeof(BeaverGeneratorTool), nameof(BeaverGeneratorTool.PlaceBeavers))]
    class BeaverGeneratorToolPlaceBeaversPatcher
    {
        static bool Prefix(BeaverGeneratorTool __instance, bool isChild, int count)
        {
            return ReplayEvent.DoPrefix(() =>
            {
                CursorCoordinates? cursor = __instance._cursorCoordinatesPicker.Pick();
                if (!cursor.HasValue) return null;
                return new SpawnBeaversEvent()
                {
                    coordinates = cursor.Value.Coordinates,
                    tileCoordinates = cursor.Value.TileCoordinates,
                    isChild = isChild,
                    count = count,
                };
            });
        }
    }

    [Serializable]
    class SpawnBotsEvent : ReplayEvent
    {
        public Vector3 coordinates;
        public Vector3Int tileCoordinates;
        public int count;

        public override void Replay(IReplayContext context)
        {
            BotGeneratorTool tool = GetSingleton<DevModeService>()?.BotGeneratorTool;
            if (tool == null) return;
            CursorCoordinatesPickerPatcher.WithCursorAt(coordinates, tileCoordinates,
                () => tool.PlaceBots(count));
        }

        public override string ToActionString()
        {
            return $"Spawning {count} bot(s) at {tileCoordinates}";
        }
    }

    [HarmonyPatch(typeof(BotGeneratorTool), nameof(BotGeneratorTool.PlaceBots))]
    class BotGeneratorToolPlaceBotsPatcher
    {
        static bool Prefix(BotGeneratorTool __instance, int count)
        {
            return ReplayEvent.DoPrefix(() =>
            {
                CursorCoordinates? cursor = __instance._cursorCoordinatesPicker.Pick();
                if (!cursor.HasValue) return null;
                return new SpawnBotsEvent()
                {
                    coordinates = cursor.Value.Coordinates,
                    tileCoordinates = cursor.Value.TileCoordinates,
                    count = count,
                };
            });
        }
    }

    // ---- Water brush (dev-mode tool that adds/removes water) ----

    [Serializable]
    class WaterBrushAppliedEvent : ReplayEvent
    {
        public Vector3Int tileCoordinates;
        public bool isRemoving;
        public bool isContaminating;
        public int brushSize;
        public int brushShape;

        public override void Replay(IReplayContext context)
        {
            WaterHeightBrushTool tool = GetSingleton<DevModeService>()?.WaterHeightBrushTool;
            if (tool == null) return;

            // Paint with the brush settings of the player who painted, then
            // restore this player's own settings.
            int previousSize = tool.BrushSize;
            BrushShape previousShape = tool.BrushShape;
            tool.BrushSize = brushSize;
            tool.BrushShape = (BrushShape)brushShape;
            try
            {
                tool.ApplyBrush(tileCoordinates, isRemoving, isContaminating);
            }
            finally
            {
                tool.BrushSize = previousSize;
                tool.BrushShape = previousShape;
            }
        }

        public override string ToActionString()
        {
            return $"Water brush at {tileCoordinates} (removing: {isRemoving}, contaminating: {isContaminating})";
        }
    }

    [HarmonyPatch(typeof(WaterHeightBrushTool), nameof(WaterHeightBrushTool.ApplyBrush))]
    class WaterHeightBrushToolApplyBrushPatcher
    {
        static bool Prefix(WaterHeightBrushTool __instance, Vector3Int tileCoordinates, bool isRemoving, bool isContaminating)
        {
            return ReplayEvent.DoPrefix(() => new WaterBrushAppliedEvent()
            {
                tileCoordinates = tileCoordinates,
                isRemoving = isRemoving,
                isContaminating = isContaminating,
                brushSize = __instance.BrushSize,
                brushShape = (int)__instance.BrushShape,
            });
        }
    }
}
