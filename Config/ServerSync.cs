using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace Resound.Config
{
    // Pack id and set name, not a path: under Fika the server may be another machine.
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

        [JsonProperty("autoLevel")]
        public bool AutoLevel = true;
    }

    // The server owns the matching rules so there is one implementation; the client maps its answer onto local packs.
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
                // No server half, or an older one. Local matching still works.
                Plugin.Log.LogInfo("No Resound server config (" + e.GetType().Name + "), using local matching.");
                return null;
            }
        }
    }
}
