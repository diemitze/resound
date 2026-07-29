using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace Resound.Config
{
    // One row of the server's decision table. Carries a pack id and set name rather than a path,
    // because under Fika the server can be on a different machine to the player.
    internal class ResolvedSound
    {
        [JsonProperty("packId")]
        public string PackId = "";

        [JsonProperty("soundSet")]
        public string SoundSet = "";

        [JsonProperty("entryName")]
        public string EntryName = "";

        [JsonProperty("bodyVolume")]
        public float BodyVolume = 1f;

        [JsonProperty("tailVolume")]
        public float TailVolume = 1f;

        [JsonProperty("bodySilencedVolume")]
        public float? BodySilencedVolume;

        [JsonProperty("tailSilencedVolume")]
        public float? TailSilencedVolume;

        [JsonProperty("eventVolume")]
        public float EventVolume = 1f;

        [JsonProperty("decayBeats")]
        public float? DecayBeats;
    }

    // Asks the server which pack wins for each weapon. The server owns that decision so there is
    // only ever one implementation of the rules; the client just maps the answer onto the packs
    // it can actually see on its own disk.
    internal static class ServerSync
    {
        public const string Route = "/resound/resolve";

        public static Dictionary<string, ResolvedSound> Fetch()
        {
            try
            {
                string json = RequestHandler.GetJson(Route);

                if (string.IsNullOrEmpty(json))
                {
                    Plugin.Log.LogWarning("Server returned nothing for " + Route + "; falling back to local matching.");
                    return null;
                }

                Dictionary<string, ResolvedSound> map =
                    JsonConvert.DeserializeObject<Dictionary<string, ResolvedSound>>(json);

                if (map == null || map.Count == 0)
                {
                    Plugin.Log.LogWarning("Server sent an empty sound table; falling back to local matching.");
                    return null;
                }

                Plugin.Log.LogInfo("Server resolved " + map.Count + " weapon(s).");
                return map;
            }
            catch (Exception e)
            {
                // No server half installed, or an older one. Local matching still works, so this
                // is a downgrade rather than a failure.
                Plugin.Log.LogInfo("No Resound server config (" + e.GetType().Name + "), using local matching.");
                return null;
            }
        }
    }
}
