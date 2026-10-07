// Nothing references this optional bridge (Storey finds it by name), so without this IL2CPP's managed stripping removes
// the whole assembly from a player build, [Preserve] or not, and no building draws (docs/COLOURS.md §3.7).
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
