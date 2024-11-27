namespace Quarto.Search;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

using Quarto.Game;

struct TTEntry
{
    //public CanonicalPosition Position;
    public short Lower;
    public short Upper;

    public TTEntry() { }
}

[StructLayout(LayoutKind.Sequential)]
struct TTCluster
{
    public const int NUM_ENTRIES = 4;

    TTEntry entry0;
    TTEntry entry1;
    TTEntry entry2;
    TTEntry entry3;

    public unsafe ref TTEntry this[int idx]
    {
        get
        {
            Debug.Assert(idx >= 0 && idx < NUM_ENTRIES);

            fixed (TTCluster* self = &this)
            {
                var entries = (TTEntry*)self;
                return ref entries[idx];
            }
        }
    }
}

