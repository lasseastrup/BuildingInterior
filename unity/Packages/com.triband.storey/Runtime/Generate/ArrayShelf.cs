#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// Arrays lent out for a while and given back, so the next user takes them instead of making garbage (docs/CITY.md
    /// §7): a finished build's packed meshes rent theirs on a worker thread and return them on the main thread once
    /// uploaded. An array comes a quarter of a power of two at a time from 1,024 elements up (at most a quarter more
    /// than asked for), with whatever it held last. At most <see cref="KeepBytes"/> are kept, across every element type;
    /// the rest are left to the collector. Thread-safe.
    /// </summary>
    public static class ArrayShelf
    {
        /// <summary>The most bytes of arrays kept for reuse.</summary>
        public static long KeepBytes = 8L << 20;

        internal static readonly object Gate = new object();
        internal static long Kept;
        static readonly List<Action> clears = new List<Action>();

        internal static void Register(Action clear) { lock (Gate) clears.Add(clear); }

        /// <summary>The bytes kept now.</summary>
        public static long KeptBytes { get { lock (Gate) return Kept; } }

        /// <summary>Let every kept array go (the tests' allocation counts start from none).</summary>
        public static void Clear() { lock (Gate) { foreach (var c in clears) c(); Kept = 0; } }

        /// <summary>The shelf an array of at least <paramref name="n"/> elements is on, and its length.</summary>
        internal static int Bucket(int n, out int length)
        {
            if (n <= 1024) { length = 1024; return 0; }
            int e = 10; while (e < 30 && (1 << (e + 1)) < n) e++;
            int b = 1 << e;   // b < n <= 2b
            int q = n <= b + (b >> 2) ? 1 : n <= b + (b >> 1) ? 2 : n <= b + 3 * (b >> 2) ? 3 : 4;
            length = q == 4 ? b << 1 : b + q * (b >> 2);
            return (e - 10) * 4 + q;
        }
    }

    /// <summary>The shelf of one element type (<see cref="ArrayShelf"/>).</summary>
    public static class ArrayShelf<T> where T : struct
    {
        static readonly Stack<T[]>?[] shelves = new Stack<T[]>?[4 * 21 + 1];
        static readonly int size = Marshal.SizeOf(typeof(T));

        static ArrayShelf() => ArrayShelf.Register(() => { for (int i = 0; i < shelves.Length; i++) shelves[i]?.Clear(); });

        /// <summary>
        /// An array of at least <paramref name="n"/> elements: a kept one if there is one at most twice that long (the
        /// shortest), holding what it held, else a new one.
        /// </summary>
        public static T[] Rent(int n)
        {
            if (n <= 0) return Array.Empty<T>();
            int b = ArrayShelf.Bucket(n, out int length);
            lock (ArrayShelf.Gate)
                for (int k = b; k < shelves.Length && k <= b + 4; k++)
                {
                    var s = shelves[k];
                    if (s != null && s.Count > 0) { var a = s.Pop(); ArrayShelf.Kept -= (long)a.Length * size; return a; }
                }
            return new T[length];
        }

        /// <summary>Give an array back (one from <see cref="Rent"/>; others are ignored). Nothing may use it after.</summary>
        public static void Return(T[]? a)
        {
            if (a == null || a.Length < 1024) return;
            int b = ArrayShelf.Bucket(a.Length, out int length);
            if (length != a.Length) return;
            long bytes = (long)a.Length * size;
            lock (ArrayShelf.Gate)
            {
                if (ArrayShelf.Kept + bytes > ArrayShelf.KeepBytes) return;
                (shelves[b] ??= new Stack<T[]>()).Push(a);
                ArrayShelf.Kept += bytes;
            }
        }
    }
}
