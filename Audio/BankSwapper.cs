using System.Collections.Generic;
using System.Reflection;
using Resound.Config;
using EFT;
using UnityEngine;

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
            // _firingLoopLength is read off whichever body bank matches this, so a suppressor
            // going on or coming off has to re-apply even when the weapon has not changed.
            public bool Silenced;
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

        public static void Apply(WeaponSoundPlayer player, string templateId, SoundProfile profile)
        {
            if (player == null)
                return;

            int id = player.GetInstanceID();
            Applied.TryGetValue(id, out State state);

            if (state != null && !ReferenceEquals(state.Owner, player))
                state = null;

            SoundSet set = profile == null ? null : ClipLibrary.Get(profile.SetDir);

            // Comparing set identity is what makes a clip reload re-apply.
            if (state != null && state.TemplateId == templateId && ReferenceEquals(state.Set, set) &&
                state.Silenced == player.IsSilenced)
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
            state.Set = set;
            state.Silenced = player.IsSilenced;

            if (profile == null)
            {
                RefreshFiringLoopLength(player);
                return;
            }

            if (set == null)
            {
                Plugin.Log.LogWarning("No clips for " + profile.Label + " at " + profile.SetDir +
                                      ", staying on vanilla.");
                RefreshFiringLoopLength(player);
                return;
            }

            // Only an automatic weapon's body is a loop; tails are always one-shots.
            bool loop = player.IsAutoWeapon;

            float bodyVol = profile.BodyVolume;
            float tailVol = profile.TailVolume;
            float bodySilVol = profile.BodySilencedVolume ?? bodyVol;
            float tailSilVol = profile.TailSilencedVolume ?? tailVol;
            float decay = profile.DecayBeats ?? LoopBuilder.DefaultDecayBeats;

            player.Body = Apply(state.Body, set.Body, loop, bodyVol, decay) ?? player.Body;
            player.Tail = Apply(state.Tail, set.Tail, false, tailVol, decay) ?? player.Tail;
            player.Doublet = Apply(state.Doublet, set.Doublet, false, bodyVol, decay) ?? player.Doublet;
            player.BodySilenced = Apply(state.BodySilenced, set.BodySilenced, loop, bodySilVol, decay) ?? player.BodySilenced;
            player.TailSilenced = Apply(state.TailSilenced, set.TailSilenced, false, tailSilVol, decay) ?? player.TailSilenced;
            player.DoubletSilenced = Apply(state.DoubletSilenced, set.DoubletSilenced, false, bodySilVol, decay) ?? player.DoubletSilenced;

            RefreshFiringLoopLength(player);

            Plugin.Log.LogInfo("Swapped " + templateId + " -> " + profile.Label + " (auto=" + loop + ")");

            if (Plugin.Debug != null && Plugin.Debug.Value)
            {
                // Report whichever path is actually live, or a suppressed problem reads clean.
                bool sil = player.IsSilenced;
                AudioClip[] bodyClips = sil ? set.BodySilenced : set.Body;
                float clipLen = bodyClips != null && bodyClips.Length > 0 && bodyClips[0] != null
                    ? bodyClips[0].length
                    : -1f;

                SoundBank liveBody = sil ? player.BodySilenced : player.Body;
                SoundBank liveTail = sil ? player.TailSilenced : player.Tail;

                Plugin.Dbg("  silenced=" + sil +
                           ", vol body/tail=" + (sil ? bodySilVol : bodyVol) + "/" + (sil ? tailSilVol : tailVol) +
                           ", bodyClip=" + clipLen + "s" +
                           ", bodyBank=" + liveBody?.name + " len=" + liveBody?.ClipLength +
                           " baseVol=" + liveBody?.BaseVolume +
                           ", tailBank=" + liveTail?.name + " baseVol=" + liveTail?.BaseVolume +
                           ", firingLoopLength=" + FiringLoopLengthField?.GetValue(player));
            }
        }

        // No clips for this slot still means the volume applies: the vanilla bank is kept, but
        // rescaled, so it stays balanced against the slots the set does replace.
        private static SoundBank Apply(SoundBank original, AudioClip[] clips, bool loop, float volume,
            float decayBeats)
        {
            if (original == null)
                return null;

            if (clips == null)
                return BankFactory.Rescale(original, volume);

            return BankFactory.Clone(original, clips, loop, volume, decayBeats);
        }
    }
}
