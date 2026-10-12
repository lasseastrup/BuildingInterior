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
    /// (<see cref="KeepBytes"/>); one given back past that is let go.
    /// </summary>
    public static class Chunks
    {
        /// <summary>The elements a chunk holds, 2,048: 48 KB of positions or packed vertices, 8 KB of indices.</summary>
        public const int Shift = 11, Size = 1 << Shift, Mask = Size - 1;

        /// <summary>The most bytes of chunks kept for the next builds, on all threads together.</summary>
        public static long KeepBytes = 32L << 20;

        internal static readonly object Gate = new object();
        internal static long Kept, made;
        static readonly List<Action> clears = new List<Action>();

        internal static void Register(Action clear) { lock (Gate) clears.Add(clear); }

        /// <summary>The bytes of chunks kept now.</summary>
        public static long KeptBytes { get { lock (Gate) return Kept; } }

        /// <summary>The chunks made so far, of every type (one taken from those kept isn't made again).</summary>
        public static long Made => Interlocked.Read(ref made);

        /// <summary>Let every kept chunk go (the tests' allocation counts start from none).</summary>
        public static void Clear() { lock (Gate) { foreach (var c in clears) c(); Kept = 0; } }
    }

    /// <summary>The chunks of one element type (<see cref="Chunks"/>).</summary>
    public static class Chunks<T> where T : struct
    {
        static readonly Stack<T[]> free = new Stack<T[]>();

        /// <summary>A chunk's bytes.</summary>
        public static readonly int Bytes = Chunks.Size * Marshal.SizeOf(typeof(T));

        static Chunks() => Chunks.Register(() => free.Clear());

        /// <summary>A chunk, holding what it last held: whoever takes it writes an element before reading it.</summary>
        public static T[] Rent()
        {
            lock (Chunks.Gate) if (free.Count > 0) { Chunks.Kept -= Bytes; return free.Pop(); }
            Interlocked.Increment(ref Chunks.made);
            return new T[Chunks.Size];
        }

        /// <summary>Give a chunk back (one from <see cref="Rent"/>). Nothing may use it after.</summary>
        public static void Return(T[] chunk)
        {
            lock (Chunks.Gate)
            {
                if (Chunks.Kept + Bytes > Chunks.KeepBytes) return;
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
            get
            {
                if ((uint)i >= (uint)count) throw new ArgumentOutOfRangeException(nameof(i), $"{i} of {count}");
                return ref chunks[i >> Chunks.Shift][i & Chunks.Mask];
            }
        }

        T IReadOnlyList<T>.this[int i] => this[i];

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
}
