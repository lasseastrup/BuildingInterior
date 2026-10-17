#nullable enable
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Triband.Storey.Lod
{
    /// <summary>
    /// Who uses each of a set of things, and which to let go (a layout's parse, docs/PERFORMANCE.md §6.5): a thing no one
    /// uses any more is kept among the last few let go, for a quick return (a player turning back into a district they
    /// just left), and let go when it falls off their end. Things are told apart by reference. Engine-free; one thread at
    /// a time (Unity's main thread).
    /// </summary>
    public sealed class Users<T> where T : class
    {
        readonly Dictionary<T, int> counts = new Dictionary<T, int>(ByReference.Instance);
        readonly List<T> unused = new List<T>();   // no one's, the last let go at the end
        int keep;

        /// <param name="keep">How many things no one uses are kept (<see cref="Keep"/>).</param>
        public Users(int keep) { Keep = keep; }

        /// <summary>How many things no one uses are kept, the last let go. Lowered, the oldest go at the next <see cref="Trim"/>.</summary>
        public int Keep { get => keep; set => keep = value < 0 ? 0 : value; }

        /// <summary>The users of <paramref name="t"/> now.</summary>
        public int Count(T t) => counts.TryGetValue(t, out int n) ? n : 0;

        /// <summary>No one uses <paramref name="t"/>, and it is kept for a return.</summary>
        public bool Kept(T t) => IndexOf(t) >= 0;

        /// <summary>One more user of <paramref name="t"/>: taken back from the kept ones if it is among them.</summary>
        public void Use(T t)
        {
            counts[t] = Count(t) + 1;
            int i = IndexOf(t);
            if (i >= 0) unused.RemoveAt(i);
        }

        /// <summary>
        /// One user fewer of <paramref name="t"/>. With none left it is kept, and what falls off the kept ones' end is
        /// added to <paramref name="letGo"/>, to let go. A thing no one uses isn't released again: nothing happens.
        /// </summary>
        public void Release(T t, List<T> letGo)
        {
            if (!counts.TryGetValue(t, out int n)) return;
            if (n > 1) { counts[t] = n - 1; return; }
            counts.Remove(t); unused.Add(t);
            Trim(letGo);
        }

        /// <summary>The kept things past <see cref="Keep"/>, oldest first, into <paramref name="letGo"/>.</summary>
        public void Trim(List<T> letGo)
        {
            int over = unused.Count - keep;
            if (over <= 0) return;
            for (int i = 0; i < over; i++) letGo.Add(unused[i]);
            unused.RemoveRange(0, over);
        }

        /// <summary>Forget every user and every kept thing (a new Play session), letting nothing go.</summary>
        public void Clear() { counts.Clear(); unused.Clear(); }

        int IndexOf(T t)
        {
            for (int i = 0; i < unused.Count; i++) if (ReferenceEquals(unused[i], t)) return i;
            return -1;
        }

        sealed class ByReference : IEqualityComparer<T>
        {
            public static readonly ByReference Instance = new ByReference();
            public bool Equals(T? a, T? b) => ReferenceEquals(a, b);
            public int GetHashCode(T t) => RuntimeHelpers.GetHashCode(t);
        }
    }
}
