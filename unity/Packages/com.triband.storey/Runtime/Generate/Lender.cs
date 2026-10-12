#nullable enable
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// Working state lent out and given back, by any thread (a lock each way): for state too big to make on every call,
    /// held by none of the threads. Storey keeps no state per thread: Unity's manual warns that the .NET
    /// <c>[ThreadStatic]</c> attribute crashes a Unity script, so working state is lent like this or belongs to an
    /// object one thread uses at a time (a <see cref="MeshBuilder"/>).
    /// </summary>
    internal sealed class Lender<T> where T : class, new()
    {
        readonly Stack<T> free = new Stack<T>();
        readonly int keep;

        /// <param name="keep">How many given back are kept for the next to take; past that they are let go.</param>
        public Lender(int keep) { this.keep = keep; }

        public T Take() { lock (free) if (free.Count > 0) return free.Pop(); return new T(); }

        /// <summary>Give back what <see cref="Take"/> lent. Nothing may use it after.</summary>
        public void Give(T item) { lock (free) if (free.Count < keep) free.Push(item); }
    }
}
