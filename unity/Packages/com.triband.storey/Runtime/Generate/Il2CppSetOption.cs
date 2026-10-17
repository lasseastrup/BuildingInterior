#nullable enable
using System;

namespace Unity.IL2CPP.CompilerServices
{
    // IL2CPP's own attribute, matched by its name: a copy here keeps the Runtime assembly engine-free (docs/PERFORMANCE.md
    // §8.8). Internal, so a project's copy never clashes with it.
    internal enum Option { NullChecks = 1, ArrayBoundsChecks = 2, DivideByZeroChecks = 3 }

    /// <summary>Turns off a check IL2CPP adds, on a member whose code makes it already (the chunk lists' range checks).</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
    internal sealed class Il2CppSetOptionAttribute : Attribute
    {
        public Option Option { get; }
        public object Value { get; }
        public Il2CppSetOptionAttribute(Option option, object value) { Option = option; Value = value; }
    }
}
