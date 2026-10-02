#nullable enable
using System.Globalization;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// Where Play mode should start (docs/PLAY.md, slice 5.4): the editor's <b>Play from this floor</b> leaves a request
    /// here before it enters Play mode, and the play kit spawns there. Kept in PlayerPrefs, which survive the domain
    /// reload on entering Play mode; read once.
    /// </summary>
    public static class PlayFrom
    {
        const string Key = "Triband.Storey.PlayFrom";

        /// <summary>Ask the next Play mode to start at a point of a site (world space, the feet), facing <paramref name="yaw"/>.</summary>
        public static void Request(string siteName, Vector3 feet, float yaw)
        {
            var c = CultureInfo.InvariantCulture;
            PlayerPrefs.SetString(Key, string.Join("|", siteName, feet.x.ToString("R", c), feet.y.ToString("R", c), feet.z.ToString("R", c), yaw.ToString("R", c)));
            PlayerPrefs.Save();
        }

        /// <summary>A request is waiting.</summary>
        public static bool Pending => PlayerPrefs.HasKey(Key);

        /// <summary>The request, if there is one; it is cleared.</summary>
        public static (string site, Vector3 feet, float yaw)? Take()
        {
            if (!PlayerPrefs.HasKey(Key)) return null;
            var parts = PlayerPrefs.GetString(Key).Split('|');
            PlayerPrefs.DeleteKey(Key);
            var c = CultureInfo.InvariantCulture;
            if (parts.Length != 5) return null;
            return (parts[0], new Vector3(float.Parse(parts[1], c), float.Parse(parts[2], c), float.Parse(parts[3], c)), float.Parse(parts[4], c));
        }
    }
}
