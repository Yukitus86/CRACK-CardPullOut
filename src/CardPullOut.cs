using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CardPullOut
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "yukitus.crack.cardpullout";
        public const string PluginName = "CardPullOut";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> PullMargin;
        internal static ConfigEntry<float> TautTime;
        internal static ConfigEntry<bool> ReleaseImmediately;
        internal static ConfigEntry<bool> LogEjects;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "Pull the ID badge out of the reader automatically when you walk away and the cable goes taut.");
            PullMargin = Config.Bind("General", "PullMargin", 0.2f,
                new ConfigDescription(
                    "How far (in meters) you have to stretch the cable past its length before the badge pops out. " +
                    "Smaller = pops out sooner.",
                    new AcceptableValueRange<float>(0f, 3f)));
            TautTime = Config.Bind("General", "TautTime", 0.15f,
                new ConfigDescription(
                    "The badge also pops out if the cable stays taut for this many seconds, even if you don't pull hard. " +
                    "0 = pop out the instant the cable goes taut.",
                    new AcceptableValueRange<float>(0f, 5f)));
            ReleaseImmediately = Config.Bind("General", "ReleaseImmediately", true,
                "Stop the cable from holding you back the moment the badge pops out, so you keep walking smoothly.");
            LogEjects = Config.Bind("Debug", "LogEjects", false,
                "Write a line to the BepInEx log every time the badge is pulled out.");

            new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }
    }

    [HarmonyPatch(typeof(IdBadge), "LateUpdate")]
    internal static class IdBadgeLateUpdatePatch
    {
        private class BadgeState
        {
            public PlayerCharacter Character;
            public float TautTimer;
        }

        private static readonly ConditionalWeakTable<IdBadge, BadgeState> States =
            new ConditionalWeakTable<IdBadge, BadgeState>();

        private static readonly AccessTools.FieldRef<IdBadge, PlayerItemHolder> HolderRef =
            AccessTools.FieldRefAccess<IdBadge, PlayerItemHolder>("holder");

        private static readonly AccessTools.FieldRef<PlayerCharacter, bool> IsTetheredRef =
            AccessTools.FieldRefAccess<PlayerCharacter, bool>("_isTethered");

        private static readonly AccessTools.FieldRef<PlayerCharacter, Vector3> AnchorRef =
            AccessTools.FieldRefAccess<PlayerCharacter, Vector3>("_tetherAnchor");

        private static readonly AccessTools.FieldRef<PlayerCharacter, float> RadiusRef =
            AccessTools.FieldRefAccess<PlayerCharacter, float>("_tetherRadius");

        private static readonly AccessTools.FieldRef<PlayerCharacter, Transform> OriginRef =
            AccessTools.FieldRefAccess<PlayerCharacter, Transform>("_tetherOrigin");

        private static readonly AccessTools.FieldRef<PlayerCharacter, TetherRope> RopeRef =
            AccessTools.FieldRefAccess<PlayerCharacter, TetherRope>("_tetherRope");

        private static void Postfix(IdBadge __instance)
        {
            if (!Plugin.Enabled.Value)
                return;

            // Only the local player's own badge, and only while it sits in a reader.
            // (Vanilla may already have ejected it this frame; then the state is no longer InReader.)
            if (__instance.CurrentState != IdBadge.State.InReader || __instance.CurrentReader == null)
                return;
            Player owner = __instance.OwnerPlayer;
            if (owner == null || !owner.isOwned)
                return;

            BadgeState state = States.GetOrCreateValue(__instance);
            if (state.Character == null)
                state.Character = owner.GetComponentInChildren<PlayerCharacter>();
            PlayerCharacter character = state.Character;
            if (character == null || !IsTetheredRef(character))
            {
                state.TautTimer = 0f;
                return;
            }

            float excess = GetTetherExcess(character, owner);
            if (excess <= 0f)
            {
                state.TautTimer = 0f;
                return;
            }

            state.TautTimer += Time.deltaTime;
            if (excess < Plugin.PullMargin.Value && state.TautTimer < Plugin.TautTime.Value)
                return;

            state.TautTimer = 0f;
            PullOut(__instance, character, excess);
        }

        // Same measurement PlayerCharacter uses for its tether spring:
        // positive = how far the cable is stretched past its length.
        private static float GetTetherExcess(PlayerCharacter character, Player owner)
        {
            float radius = RadiusRef(character);
            if (radius <= 0f)
                return 0f;

            Transform origin = OriginRef(character);
            TetherRope rope = RopeRef(character);
            float length;
            if (rope != null && rope.WrapCount > 0)
            {
                length = rope.GetRopeLength();
            }
            else
            {
                Vector3 from = origin != null ? origin.position : owner.transform.position;
                length = Vector3.Distance(from, AnchorRef(character));
            }
            return length - radius;
        }

        // Mirrors IdBadge.CheckAutoEject: local tween back to the pocket + server request,
        // exactly what pressing E on the reader does.
        private static void PullOut(IdBadge badge, PlayerCharacter character, float excess)
        {
            PlayerItemHolder holder = HolderRef(badge);
            if (holder == null)
                return;
            NetworkItemSync sync = holder.GetComponent<NetworkItemSync>();
            if (sync == null)
                sync = holder.GetComponentInParent<NetworkItemSync>();
            if (sync == null)
                return;

            BadgeReader reader = badge.CurrentReader;
            badge.ReturnFromReader();
            sync.RequestBadgeRetrieve(reader);

            if (Plugin.ReleaseImmediately.Value)
                character.ClearTether();

            if (Plugin.LogEjects.Value)
                Plugin.Log.LogInfo($"Badge pulled out of reader '{reader.name}' (cable stretched {excess:0.00} m).");
        }
    }
}
