using System.Collections.Generic;
using System.Reflection;
using Resound.Config;
using EFT;

namespace Resound.Audio
{
    // Applies a sound set to a live WeaponSoundPlayer and restores vanilla when needed.
    internal static class BankSwapper
    {
        private class State
        {
            // Unity recycles instance ids, so the id alone is not a safe key.
            public WeaponSoundPlayer Owner;
            public string TemplateId;
            public SoundSet Set;
            public SoundBank Body, Tail, Doublet, BodySilenced, TailSilenced, DoubletSilenced;
        }

        private static readonly Dictionary<int, State> Applied = new Dictionary<int, State>();

        private static readonly FieldInfo FiringLoopLengthField =
            typeof(WeaponSoundPlayer).GetField("_firingLoopLength", BindingFlags.Instance | BindingFlags.NonPublic);

        // Init() caches this from the body bank at draw time and it gates source release,
        // so swapping later without recomputing cuts long clips off mid-shot.
        private static void RefreshFiringLoopLength(WeaponSoundPlayer player)
        {
            if (FiringLoopLengthField == null)
                return;

            SoundBank bank = player.IsSilenced ? player.BodySilenced : player.Body;
            if (bank == null)
                bank = player.Body;

            if (bank != null)
                FiringLoopLengthField.SetValue(player, bank.ClipLength);
        }

        public static void Apply(WeaponSoundPlayer player, string templateId, WeaponMapping mapping)
        {
            if (player == null)
                return;

            int id = player.GetInstanceID();
            Applied.TryGetValue(id, out State state);

            if (state != null && !ReferenceEquals(state.Owner, player))
                state = null;

            SoundSet current = mapping == null
                ? null
                : ClipLibrary.Get(Plugin.SoundsRoot, mapping.SoundSet);

            // Comparing set identity is what makes a clip reload re-apply.
            if (state != null && state.TemplateId == templateId && ReferenceEquals(state.Set, current))
                return;

            if (state == null)
            {
                state = new State
                {
                    Owner = player,
                    Body = player.Body,
                    Tail = player.Tail,
                    Doublet = player.Doublet,
                    BodySilenced = player.BodySilenced,
                    TailSilenced = player.TailSilenced,
                    DoubletSilenced = player.DoubletSilenced
                };
                Applied[id] = state;
            }
            else
            {
                // Prefabs are pooled, so this instance may now hold a different gun.
                player.Body = state.Body;
                player.Tail = state.Tail;
                player.Doublet = state.Doublet;
                player.BodySilenced = state.BodySilenced;
                player.TailSilenced = state.TailSilenced;
                player.DoubletSilenced = state.DoubletSilenced;
            }

            state.TemplateId = templateId;
            state.Set = current;

            if (mapping == null)
            {
                RefreshFiringLoopLength(player);
                return;
            }

            if (current == null)
            {
                Plugin.Log.LogWarning("No clips for set '" + mapping.SoundSet + "' (" + mapping.Name +
                                      "), staying on vanilla.");
                RefreshFiringLoopLength(player);
                return;
            }

            SoundSet set = current;

            // Only an automatic weapon's body is a loop; tails are always one-shots.
            bool loop = player.IsAutoWeapon;

            float bodyVol = mapping.BodyVolume;
            float tailVol = mapping.TailVolume;

            player.Body = Swap(state.Body, set.Body, loop, bodyVol) ?? player.Body;
            player.Tail = Swap(state.Tail, set.Tail, false, tailVol) ?? player.Tail;
            player.Doublet = Swap(state.Doublet, set.Doublet, false, bodyVol) ?? player.Doublet;
            player.BodySilenced = Swap(state.BodySilenced, set.BodySilenced, loop, bodyVol) ?? player.BodySilenced;
            player.TailSilenced = Swap(state.TailSilenced, set.TailSilenced, false, tailVol) ?? player.TailSilenced;
            player.DoubletSilenced = Swap(state.DoubletSilenced, set.DoubletSilenced, false, bodyVol) ?? player.DoubletSilenced;

            RefreshFiringLoopLength(player);

            Plugin.Log.LogInfo("Swapped " + mapping.Name + " -> '" + mapping.SoundSet + "' (auto=" + loop + ")");

            if (Plugin.Debug != null && Plugin.Debug.Value)
            {
                float clipLen = set.Body != null && set.Body.Length > 0 && set.Body[0] != null
                    ? set.Body[0].length
                    : -1f;

                Plugin.Dbg("  vol body/tail=" + bodyVol + "/" + tailVol +
                           ", bodyClip=" + clipLen + "s" +
                           ", bankLength=" + player.Body?.ClipLength +
                           ", firingLoopLength=" + FiringLoopLengthField?.GetValue(player));
            }
        }

        private static SoundBank Swap(SoundBank original, UnityEngine.AudioClip[] clips, bool loop, float volume)
        {
            if (original == null || clips == null)
                return null;

            return BankFactory.Clone(original, clips, loop, volume);
        }
    }
}
