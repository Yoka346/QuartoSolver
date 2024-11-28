namespace Quarto.Search;

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Quarto.Game;

struct TTEntry
{
    public CanonicalPosition Position;
    public sbyte Lower;
    public sbyte Upper;
    public byte Depth;

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

class TranspositionTable
{
    readonly ulong SIZE;
    readonly TTCluster[] tt;

    public TranspositionTable(ulong sizeBytes)
    {
        var numClusters = sizeBytes / (ulong)Marshal.SizeOf<TTCluster>();
        var floorLog2 = 63 - BitOperations.LeadingZeroCount(numClusters);
        this.SIZE = 1UL << floorLog2;
        this.tt = new TTCluster[this.SIZE];
    }

    public void Clear() => Array.Clear(this.tt);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public ref TTEntry GetEntry(ref CanonicalPosition pos, out bool hit)
    {
        var idx = pos.ComputeHashCode() & (this.SIZE - 1);
        ref var entries = ref this.tt[idx];

        for(var i = 0; i < TTCluster.NUM_ENTRIES; i++)
        {
            ref var entry = ref entries[i];

            if(entry.Depth == 0)
            {
                hit = false;
                return ref entries[i];
            }
            
            if(entry.Position.EqualsTo(ref pos))
            {
                hit = true;
                return ref entries[i];
            }
        }

        // overwrite an entry
        ref var replace = ref entries[0];
        for(var i = 1; i < TTCluster.NUM_ENTRIES; i++)
        {
            if(replace.Depth > entries[i].Depth)
                replace = ref entries[i];
        }

        hit = false;
        return ref replace;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SaveAt(ref TTEntry entry, ref CanonicalPosition pos, int lower, int upper, int depth)
    {
        entry.Position = pos;
        entry.Lower = (sbyte)lower;
        entry.Upper = (sbyte)upper;
        entry.Depth = (byte)depth;
    }
}

