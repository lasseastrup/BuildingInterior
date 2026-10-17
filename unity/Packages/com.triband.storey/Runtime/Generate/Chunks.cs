#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// The fixed-size chunks every mesh builder's lists and every packed mesh are made of (docs/CITY.md §7). A
    /// <see cref="ChunkList{T}"/> grows a chunk at a time and never copies, and gives its chunks back here when it is
    /// emptied; any chunk of a type fits any list of it, so a build takes the chunks the last one gave back instead of
    /// making garbage, whatever the sizes of the two buildings. The chunks kept between builds are capped
    /// (<see cref="KeepBytes"/>), each type to its share of what builds take: a builder gives its chunks back before its
    /// packed mesh does, and would otherwise fill the cap and leave none for the next packing. One given back past that is
    /// let go.
    /// </summary>
    public static class Chunks
    {
        /// <summary>The elements a chunk holds, 2,048: 48 KB of positions or packed vertices, 8 KB of indices.</summary>
        public const int Shift = 11, Size = 1 << Shift, Mask = Size - 1;

        /// <summary>
        /// The most bytes of chunks kept for the next builds, on all threads together. Lowered, it lets go at once of the
        /// chunks kept past it, each type down to its share (otherwise only a chunk given back is held to it, and a site
        /// standing still gives none back).
        /// </summary>
        public static long KeepBytes
        {
            get { lock (Gate) return Keep; }
            set { lock (Gate) { bool lower = value < Keep; Keep = value; if (lower) foreach (var t in trims) t(); } }
        }

        internal static readonly object Gate = new object();
        internal static long Kept, made, Taken;   // Taken: the bytes of chunks every type has handed out so far
        internal static long Keep = 16L << 20;
        static readonly List<Action> clears = new List<Action>(), trims = new List<Action>();

        internal static void Register(Action clear, Action trim) { lock (Gate) { clears.Add(clear); trims.Add(trim); } }

        /// <summary>The bytes of chunks kept now.</summary>
        public static long KeptBytes { get { lock (Gate) return Kept; } }

        /// <summary>The chunks made so far, of every type (one taken from those kept isn't made again).</summary>
        public static long Made => Interlocked.Read(ref made);

        /// <summary>Let every kept chunk go, and what each type has handed out (the tests' counts start from none).</summary>
        public static void Clear() { lock (Gate) { foreach (var c in clears) c(); Kept = 0; Taken = 0; } }
    }

    /// <summary>The chunks of one element type (<see cref="Chunks"/>).</summary>
    public static class Chunks<T> where T : struct
    {
        static readonly Stack<T[]> free = new Stack<T[]>();
        static long taken;   // the bytes of chunks of this type handed out so far: its share of the cap

        /// <summary>A chunk's bytes.</summary>
        public static readonly int Bytes = Chunks.Size * Marshal.SizeOf(typeof(T));

        static Chunks() => Chunks.Register(() => { free.Clear(); taken = 0; }, Trim);

        // down to this type's share of a lowered cap, as a chunk given back is held to (under the gate)
        static void Trim()
        {
            double share = (double)Chunks.Keep * taken / Math.Max(1, Chunks.Taken);
            while (free.Count > 0 && (long)free.Count * Bytes > share) { free.Pop(); Chunks.Kept -= Bytes; }
        }

        /// <summary>The bytes of chunks of this type kept now.</summary>
        public static long KeptBytes { get { lock (Chunks.Gate) return (long)free.Count * Bytes; } }

        /// <summary>A chunk, holding what it last held: whoever takes it writes an element before reading it.</summary>
        public static T[] Rent()
        {
            lock (Chunks.Gate)
            {
                taken += Bytes; Chunks.Taken += Bytes;
                if (free.Count > 0) { Chunks.Kept -= Bytes; return free.Pop(); }
            }
            Interlocked.Increment(ref Chunks.made);
            return new T[Chunks.Size];
        }

        /// <summary>
        /// Give a chunk back (one from <see cref="Rent"/>). Nothing may use it after. It is kept while the chunks kept are
        /// under the cap, and this type's under its share of it: what it has handed out of all the chunks handed out.
        /// </summary>
        public static void Return(T[] chunk)
        {
            lock (Chunks.Gate)
            {
                if (Chunks.Kept + Bytes > Chunks.Keep) return;
                long kept = (long)free.Count * Bytes;
                if (kept + Bytes > (double)Chunks.Keep * taken / Math.Max(1, Chunks.Taken)) return;
                free.Push(chunk); Chunks.Kept += Bytes;
            }
        }
    }

    /// <summary>
    /// A list made of <see cref="Chunks"/>: it grows a chunk at a time without copying, and gives its chunks back when it
    /// is emptied (<see cref="Clear"/>) or cut short (<see cref="Truncate"/>). The indexer returns a reference.
    /// </summary>
    public sealed class ChunkList<T> : IReadOnlyList<T> where T : struct
    {
        T[][] chunks = Array.Empty<T[]>();
        int used, count;
        // the chunk holding element count, while count is inside one (not a multiple of the chunk size): Add writes there
        T[] tail = Array.Empty<T>();

        public int Count => count;
        /// <summary>The elements the chunks held now have room for.</summary>
        public int Capacity => used << Chunks.Shift;
        /// <summary>The chunks held now.</summary>
        public int ChunkCount => used;

        /// <summary>Chunk <paramref name="c"/>: elements from c × <see cref="Chunks.Size"/> on, the last one filled up to <see cref="Count"/>.</summary>
        public T[] Chunk(int c) => (uint)c < (uint)used ? chunks[c] : throw new ArgumentOutOfRangeException(nameof(c));

        public ref T this[int i]
        {
            // inlined, with the throw apart: a throw with its message in the getter (string.Format and boxing) kept the
            // compiler from inlining it, and packing reads every vertex through it (docs/PERFORMANCE.md §8.2). IL2CPP's own
            // checks besides add nothing: the range is checked here, and every chunk below count is there (§8.8)
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            [global::Unity.IL2CPP.CompilerServices.Il2CppSetOption(global::Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
            [global::Unity.IL2CPP.CompilerServices.Il2CppSetOption(global::Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
            get
            {
                if ((uint)i >= (uint)count) OutOfRange(i, count);
                return ref chunks[i >> Chunks.Shift][i & Chunks.Mask];
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void OutOfRange(int i, int count) => throw new ArgumentOutOfRangeException(nameof(i), $"{i} of {count}");

        T IReadOnlyList<T>.this[int i] => this[i];

        [global::Unity.IL2CPP.CompilerServices.Il2CppSetOption(global::Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
        [global::Unity.IL2CPP.CompilerServices.Il2CppSetOption(global::Unity.IL2CPP.CompilerServices.Option.NullChecks, false)]
        public void Add(T item)
        {
            int o = count & Chunks.Mask;
            if (o == 0) tail = (count >> Chunks.Shift) < used ? chunks[count >> Chunks.Shift] : Grow();
            tail[o] = item;
            count++;
        }

        public void AddRange(IEnumerable<T> items) { foreach (var x in items) Add(x); }

        /// <summary>
        /// <see cref="Count"/> becomes <paramref name="n"/>. Elements past the old count hold whatever their chunks held:
        /// write each before reading it.
        /// </summary>
        public void Resize(int n)
        {
            if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
            while (used << Chunks.Shift < n) Grow();
            if (n < count) Truncate(n); else SetCount(n);
        }

        /// <summary>Keep the first <paramref name="n"/> elements, giving back the chunks past them.</summary>
        public void Truncate(int n)
        {
            if ((uint)n > (uint)count) throw new ArgumentOutOfRangeException(nameof(n), $"{n} of {count}");
            Release((n + Chunks.Mask) >> Chunks.Shift); SetCount(n);
        }

        /// <summary>Empty, every chunk given back.</summary>
        public void Clear() { Release(0); SetCount(0); }

        void SetCount(int n) { count = n; tail = (n & Chunks.Mask) != 0 ? chunks[n >> Chunks.Shift] : Array.Empty<T>(); }

        T[] Grow()
        {
            if (used == chunks.Length) Array.Resize(ref chunks, Math.Max(4, used * 2));
            return chunks[used++] = Chunks<T>.Rent();
        }

        void Release(int keep)
        {
            for (int c = keep; c < used; c++) { Chunks<T>.Return(chunks[c]); chunks[c] = null!; }
            if (used > keep) used = keep;
        }

        public T[] ToArray() { var a = new T[count]; CopyTo(0, a, 0, count); return a; }

        /// <summary>Copy <paramref name="n"/> elements from <paramref name="start"/> into <paramref name="to"/> at <paramref name="at"/>.</summary>
        public void CopyTo(int start, T[] to, int at, int n)
        {
            if (start < 0 || n < 0 || start + n > count) throw new ArgumentOutOfRangeException(nameof(n));
            while (n > 0)
            {
                int c = start >> Chunks.Shift, o = start & Chunks.Mask, k = Math.Min(n, Chunks.Size - o);
                Array.Copy(chunks[c], o, to, at, k);
                start += k; at += k; n -= k;
            }
        }

        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<T>
        {
            readonly ChunkList<T> list;
            int i;
            internal Enumerator(ChunkList<T> list) { this.list = list; i = -1; }
            public bool MoveNext() => ++i < list.count;
            public T Current => list.chunks[i >> Chunks.Shift][i & Chunks.Mask];
            object IEnumerator.Current => Current;
            public void Reset() => i = -1;
            public void Dispose() { }
        }
    }

    /// <summary>
    /// A per-vertex value that repeats (a normal, a colour), held as an index into a table of the distinct values: 4 bytes a
    /// vertex in <see cref="Chunks"/> instead of the value's 24. Values the comparer calls equal are one entry, the first
    /// added. Reads as a list of the values.
    /// </summary>
    public sealed class IndexedList<T> : IReadOnlyList<T> where T : struct
    {
        /// <summary>Each element's entry in <see cref="Values"/>.</summary>
        public readonly ChunkList<int> Index = new ChunkList<int>();
        /// <summary>The distinct values, in the order first added.</summary>
        public readonly List<T> Values = new List<T>();
        readonly Dictionary<T, int> lookup;
        readonly IEqualityComparer<T> eq;
        T last;
        int lastEntry = -1;

        public IndexedList(IEqualityComparer<T> eq) { this.eq = eq; lookup = new Dictionary<T, int>(eq); }

        public int Count => Index.Count;
        /// <summary>The chunks the indices hold now.</summary>
        public int ChunkCount => Index.ChunkCount;
        public T this[int i] => Values[Index[i]];

        public void Add(T value) => Index.Add(EntryOf(value));

        /// <summary>The entry of <paramref name="value"/> in <see cref="Values"/>, added if there is none equal to it.</summary>
        public int EntryOf(T value)
        {
            if (lastEntry >= 0 && eq.Equals(value, last)) return lastEntry;
            if (!lookup.TryGetValue(value, out int e)) { e = Values.Count; Values.Add(value); lookup.Add(value, e); }
            last = value; lastEntry = e;
            return e;
        }

        /// <summary>Keep the first <paramref name="n"/> elements (the table keeps every value).</summary>
        public void Truncate(int n) => Index.Truncate(n);

        /// <summary>Empty, the table too, every chunk given back.</summary>
        public void Clear() { Index.Clear(); Values.Clear(); lookup.Clear(); lastEntry = -1; }

        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<T>
        {
            readonly IndexedList<T> list;
            int i;
            internal Enumerator(IndexedList<T> list) { this.list = list; i = -1; }
            public bool MoveNext() => ++i < list.Count;
            public T Current => list[i];
            object IEnumerator.Current => Current;
            public void Reset() => i = -1;
            public void Dispose() { }
        }
    }

    /// <summary>Points equal to the bit: a table of them keeps each value as it was made, 0 and −0 apart.</summary>
    public sealed class P3Bits : IEqualityComparer<P3>
    {
        public static readonly P3Bits Instance = new P3Bits();
        public bool Equals(P3 a, P3 b) => Bits(a.x) == Bits(b.x) && Bits(a.y) == Bits(b.y) && Bits(a.z) == Bits(b.z);
        public int GetHashCode(P3 p) => HashCode.Combine(Bits(p.x), Bits(p.y), Bits(p.z));
        static long Bits(double v) => BitConverter.DoubleToInt64Bits(v);
    }
}
