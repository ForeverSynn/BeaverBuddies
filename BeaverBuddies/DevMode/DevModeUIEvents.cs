using BeaverBuddies.Events;
using HarmonyLib;
using System;
using Timberborn.Effects;
using Timberborn.Explosions;
using Timberborn.ExplosionsUI;
using Timberborn.MechanicalSystem;
using Timberborn.MechanicalSystemUI;
using Timberborn.NeedSpecs;
using Timberborn.NeedSystem;
using Timberborn.WaterSourceSystem;
using Timberborn.WaterSourceSystemUI;
using Timberborn.WellbeingUI;
using UnityEngine;
using UnityEngine.UIElements;

/**
 * Entity panel controls that are only available in developer mode (or the
 * map editor). In co-op they are recorded and replayed like regular UI
 * actions, so changing them does not desync other players.
 */
namespace BeaverBuddies.DevMode
{
    // ---- Water sources ----

    [Serializable]
    class WaterSourceStrengthSetEvent : ReplayEvent
    {
        public string entityID;
        public float strength;

        public override void Replay(IReplayContext context)
        {
            GetComponent<WaterSource>(context, entityID)?.SetSpecifiedStrength(strength);
        }

        public override string ToActionString()
        {
            return $"Setting water source {entityID} strength to {strength}";
        }
    }

    [HarmonyPatch(typeof(WaterSourceFragment), nameof(WaterSourceFragment.SetWaterSourceStrength))]
    class WaterSourceFragmentSetWaterSourceStrengthPatcher
    {
        static bool Prefix(WaterSourceFragment __instance, float value)
        {
            return ReplayEvent.DoEntityPrefix(__instance._waterSource, entityID => new WaterSourceStrengthSetEvent()
            {
                entityID = entityID,
                strength = value,
            });
        }
    }

    [Serializable]
    class WaterSourceContaminationSetEvent : ReplayEvent
    {
        public string entityID;
        public float contamination;

        public override void Replay(IReplayContext context)
        {
            GetComponent<WaterSourceContamination>(context, entityID)?.SetContamination(contamination);
        }

        public override string ToActionString()
        {
            return $"Setting water source {entityID} contamination to {contamination}";
        }
    }

    [HarmonyPatch(typeof(WaterSourceContamination), nameof(WaterSourceContamination.SetContamination))]
    class WaterSourceContaminationSetContaminationPatcher
    {
        static bool Prefix(WaterSourceContamination __instance, float strength)
        {
            // Game logic (e.g. badtides) also sets contamination during ticks.
            // That is deterministic and must not be recorded; only the
            // dev-mode UI calls this outside of a tick.
            if (DeterminismService.IsTicking) return true;
            return ReplayEvent.DoEntityPrefix(__instance, entityID => new WaterSourceContaminationSetEvent()
            {
                entityID = entityID,
                contamination = strength,
            });
        }
    }

    // ---- Unstable core explosion radius ----

    [Serializable]
    class UnstableCoreRadiusSetEvent : ReplayEvent
    {
        public string entityID;
        public int radius;

        public override void Replay(IReplayContext context)
        {
            UnstableCore core = GetComponent<UnstableCore>(context, entityID);
            if (core == null) return;
            core.SetRadius(Mathf.Clamp(radius, core.MinExplosionRadius, core.MaxExplosionRadius));
        }

        public override string ToActionString()
        {
            return $"Setting unstable core {entityID} radius to {radius}";
        }
    }

    [HarmonyPatch(typeof(UnstableCoreFragment), nameof(UnstableCoreFragment.OnRadiusChanged))]
    class UnstableCoreFragmentOnRadiusChangedPatcher
    {
        static bool Prefix(UnstableCoreFragment __instance, ChangeEvent<int> evt)
        {
            return ReplayEvent.DoEntityPrefix(__instance._unstableCore, entityID => new UnstableCoreRadiusSetEvent()
            {
                entityID = entityID,
                radius = evt.newValue,
            });
        }
    }

    // ---- Battery charge slider ----

    [Serializable]
    class BatteryChargeSetEvent : ReplayEvent
    {
        public string entityID;
        // Fraction of the battery's capacity, 0 to 1
        public float chargeLevel;

        public override void Replay(IReplayContext context)
        {
            MechanicalNode node = GetComponent<MechanicalNode>(context, entityID);
            if (node == null || node.Battery == null) return;
            // Mirrors BatteryFragment.ChangeCharge: empty the battery, then
            // charge it to the chosen level.
            node.Battery.ModifyCharge(float.MinValue);
            node.Battery.ModifyCharge(chargeLevel * node.NominalBatteryCapacity);
        }

        public override string ToActionString()
        {
            return $"Setting battery {entityID} charge to {chargeLevel:P0}";
        }
    }

    [HarmonyPatch(typeof(BatteryFragment), nameof(BatteryFragment.ChangeCharge))]
    class BatteryFragmentChangeChargePatcher
    {
        static bool Prefix(BatteryFragment __instance, ChangeEvent<float> changeEvent)
        {
            return ReplayEvent.DoEntityPrefix(__instance._mechanicalNode, entityID => new BatteryChargeSetEvent()
            {
                entityID = entityID,
                chargeLevel = changeEvent.newValue,
            });
        }
    }

    // ---- Wellbeing debug buttons (Freeze / Unfreeze / Boost needs) ----

    [Serializable]
    abstract class NeedsDevEvent : ReplayEvent
    {
        public string entityID;

        public override void Replay(IReplayContext context)
        {
            NeedManager needManager = GetComponent<NeedManager>(context, entityID);
            if (needManager == null) return;
            foreach (NeedSpec needSpec in needManager.NeedSpecs)
            {
                Apply(needManager, needSpec);
            }
        }

        protected abstract void Apply(NeedManager needManager, NeedSpec needSpec);

        public override string ToActionString()
        {
            return $"{type} for {entityID}";
        }
    }

    [Serializable]
    class FreezeNeedsEvent : NeedsDevEvent
    {
        protected override void Apply(NeedManager needManager, NeedSpec needSpec)
        {
            needManager.DisableUpdate(needSpec.Id);
        }
    }

    [Serializable]
    class UnfreezeNeedsEvent : NeedsDevEvent
    {
        protected override void Apply(NeedManager needManager, NeedSpec needSpec)
        {
            needManager.EnableUpdate(needSpec.Id);
        }
    }

    [Serializable]
    class BoostNeedsEvent : NeedsDevEvent
    {
        protected override void Apply(NeedManager needManager, NeedSpec needSpec)
        {
            // Mirrors WellbeingFragment.BoostAllNeeds
            InstantEffect effect = new InstantEffect(needSpec.Id, needManager.NeedPointsToMax(needSpec.Id), 1);
            needManager.ApplyEffect(in effect);
        }
    }

    [HarmonyPatch(typeof(WellbeingFragment), nameof(WellbeingFragment.FreezeAllNeeds))]
    class WellbeingFragmentFreezeAllNeedsPatcher
    {
        static bool Prefix(WellbeingFragment __instance)
        {
            return ReplayEvent.DoEntityPrefix(__instance._selectedNeedManager, entityID => new FreezeNeedsEvent()
            {
                entityID = entityID,
            });
        }
    }

    [HarmonyPatch(typeof(WellbeingFragment), nameof(WellbeingFragment.UnfreezeAllNeeds))]
    class WellbeingFragmentUnfreezeAllNeedsPatcher
    {
        static bool Prefix(WellbeingFragment __instance)
        {
            return ReplayEvent.DoEntityPrefix(__instance._selectedNeedManager, entityID => new UnfreezeNeedsEvent()
            {
                entityID = entityID,
            });
        }
    }

    [HarmonyPatch(typeof(WellbeingFragment), nameof(WellbeingFragment.BoostAllNeeds))]
    class WellbeingFragmentBoostAllNeedsPatcher
    {
        static bool Prefix(WellbeingFragment __instance)
        {
            return ReplayEvent.DoEntityPrefix(__instance._selectedNeedManager, entityID => new BoostNeedsEvent()
            {
                entityID = entityID,
            });
        }
    }
}
