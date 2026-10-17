#nullable enable
using System.Threading.Tasks;

namespace Triband.Storey.Lod
{
    /// <summary>
    /// A layout's text parsed on a build thread (<see cref="BuildThreads"/>), for a site that mustn't hold a frame for it
    /// (docs/CITY.md §2): the 3,000-building test city's text takes 0.1–0.2 s to parse and makes 60 MB of garbage,
    /// headless. Ask each frame: <see cref="Document"/> is null until the parse is done. With no build threads the text is
    /// parsed at once, on the caller's thread. Engine-free.
    /// </summary>
    public sealed class LayoutParse
    {
        readonly Task<StoreyDocument> task;

        /// <param name="on">The threads it is parsed on: <see cref="BuildThreads.Shared"/> unless given.</param>
        public LayoutParse(string text, BuildThreads? on = null)
        {
            Text = text;
            task = (on ?? BuildThreads.Shared).Run(() => PrototypeJson.Read(text).Document, BuildLane.Background);
        }

        /// <summary>The text being parsed.</summary>
        public string Text { get; }

        /// <summary>The layout once it is parsed, otherwise null. A parse that failed throws its exception here.</summary>
        public StoreyDocument? Document => task.IsCompleted ? task.GetAwaiter().GetResult() : null;

        /// <summary>The layout, waited for: parsed on the caller's thread if no build thread has started it.</summary>
        public StoreyDocument Wait() => task.GetAwaiter().GetResult();
    }
}
