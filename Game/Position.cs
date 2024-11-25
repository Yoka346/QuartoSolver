namespace Quarto.Game;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Text;
using Quarto.Utils;

using static QuartoConstants;

public static class QuartoConstants
{
    public const int BOARD_SIZE = 4;
    public const int NUM_SQUARES = BOARD_SIZE * BOARD_SIZE;
    public const int NUM_PIECE_PROPERTIES = 8;
    public const int NUM_PIECES = 16;
}

public enum PieceProperty : sbyte
{
    Default = -1,
    Color = 1,
    Hollow = 1 << 1,
    Height = 1 << 2,
    Shape = 1 << 3,
    Mask = 0xf
}

public unsafe struct LinkedList16
{
    const int LEN = 16;
    const int HEAD = 0;

    public int Count { readonly get; set; } = LEN;

    fixed int forwardLink[LEN + 1];
    fixed int backwardLink[LEN + 1];

    public LinkedList16()
    {
        for(var i = 0; i < LEN; i++)
            this.forwardLink[i] = i + 1;
        this.forwardLink[LEN] = 0;

        this.backwardLink[0] = LEN;
        this.backwardLink[1] = 0;
        for(var i = 2; i <= LEN; i++)
            this.backwardLink[i] = i - 1;
    } 

    public void AddAfter(int prevItem, int item)
    {
        prevItem++;
        item++;

        this.forwardLink[item] = this.forwardLink[prevItem];
        this.backwardLink[item] = prevItem;
        this.forwardLink[prevItem] = item;
        this.backwardLink[this.forwardLink[item]] = item;

        this.Count++;
    }

    public void Remove(int item)
    {
        item++;

        this.forwardLink[this.backwardLink[item]] = this.forwardLink[item];
        this.backwardLink[this.forwardLink[item]] = this.backwardLink[item];

        this.Count--;
    }

    public int GetNext(int item = HEAD - 1) 
    { 
        item++;

        Debug.Assert(item >= 0 && item <= NUM_PIECES);

        return this.forwardLink[item] - 1;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append("{ ");
        for(var i = 0; i < LEN + 1; i++)
            sb.Append(this.forwardLink[i]).Append(", ");
        sb.AppendLine("}");

        sb.Append("{ ");
        for(var i = 0; i < LEN + 1; i++)
            sb.Append(this.backwardLink[i]).Append(", ");
        sb.AppendLine("}");

        return sb.ToString();
    }
}

public unsafe struct PieceList
{
    LinkedList16 list;

    public readonly int Count => list.Count;

    public PieceList() => this.list = new LinkedList16();

    public void AddAfter(PieceProperty prevPiece, PieceProperty piece) => list.AddAfter((int)prevPiece, (int)piece);

    public void Remove(PieceProperty piece) => list.Remove((int)piece);

    public PieceProperty GetNext(PieceProperty piece = PieceProperty.Default) => (PieceProperty)list.GetNext((int)piece);
}

public readonly struct Move(PieceProperty piece, int coord)
{
    public readonly PieceProperty Piece => PIECE;
    public readonly int Coord => COORD;

    readonly PieceProperty PIECE = piece;
    readonly int COORD = coord;
}

public unsafe struct CanonicalPosition
{
    const int LEN = 9;

    public static int Length => LEN;
    
    static readonly ulong[][] HASH_RAND = new ulong[LEN][]; 

    public ushort this[int idx] 
    {
        readonly get
        {
            Debug.Assert(idx >= 0 && idx < LEN);
            return values[idx];
        }

        set
        {
            Debug.Assert(idx >= 0 && idx < LEN);
            values[idx] = value;
        }
    }

    fixed ushort values[LEN];

    static CanonicalPosition()
    {
        if(!Sse42.IsSupported && !Crc32.IsSupported)
        {
            var rand = new Random(Random.Shared.Next());
            for(var i = 0; i < HASH_RAND.Length; i++)
                HASH_RAND[i] = Enumerable.Range(0, ushort.MaxValue + 1).Select(_ => (ulong)rand.NextInt64()).ToArray();
        }
    }

    public static bool operator==(CanonicalPosition lhs, CanonicalPosition rhs)
    {
        return lhs.values[0] == rhs.values[0]
            && lhs.values[1] == rhs.values[1]
            && lhs.values[2] == rhs.values[2]
            && lhs.values[3] == rhs.values[3]
            && lhs.values[4] == rhs.values[4]
            && lhs.values[5] == rhs.values[5]
            && lhs.values[6] == rhs.values[6]
            && lhs.values[7] == rhs.values[7]
            && lhs.values[8] == rhs.values[8];
    }

    public static bool operator!=(CanonicalPosition lhs, CanonicalPosition rhs) => !(lhs == rhs);

    public readonly ulong CalcHashCode()
    {
        if(!Sse42.IsSupported && !Crc32.IsSupported)
        {
            var hashCode = 0UL;
            for(var i = 0; i < HASH_RAND.Length; i++)
                hashCode ^= HASH_RAND[i][this[i]];
            return hashCode;
        }

        fixed(ushort* values = this.values)
        {
            var crc32 = ComputeCrc32(0u, *(ulong*)values);
            crc32 = (crc32 << 32) | ComputeCrc32((uint)crc32, *((ulong*)values + 1));
            crc32 = (crc32 << 32) | ComputeCrc32((uint)crc32, values[LEN - 1]);
            return crc32;
        }
    }

    public override readonly int GetHashCode() => (int)CalcHashCode();

    public override readonly bool Equals([NotNullWhen(true)] object? obj)
    {
        if(obj is null)
            return false;

        if(obj is not CanonicalPosition pos)
            return false;

        return this == pos;
    }

    public override readonly string ToString() 
    => $"{values[0]},  {values[1]},  {values[2]},  {values[3]},  {values[4]},  {values[5]},  {values[6]},  {values[7]},  {values[8]}";

    static ulong ComputeCrc32(uint crc, ulong data)
    {
        if(Crc32.IsSupported)
        {
            if(Crc32.Arm64.IsSupported)
                return Crc32.Arm64.ComputeCrc32(crc, data);
            else
                return Crc32.ComputeCrc32(Crc32.ComputeCrc32(crc, (uint)data), (uint)(data >> 32));
        }

        if (Sse42.X64.IsSupported)
                return Sse42.X64.Crc32(crc, data);
            
        return Sse42.Crc32(Sse42.Crc32(crc, (uint)data), (uint)(data >> 32));
    }
}

public struct Position
{
    static readonly bool[] IS_QUARTO;

    static readonly ushort[] ROTATE;
    static readonly ushort[] MIRROR;
    static readonly ushort[] MID_FLIP;
    static readonly ushort[] INSIDE_OUT;

    static readonly ushort[] PATTERN_ID;


    static ReadOnlySpan<int> MID_FLIP_TRANSFORMER => 
    [
         0,   2,  1,  3,
         8,  10,  9, 11,
         4,   6,  5,  7,
        12,  14, 13, 15, 
    ];

    static ReadOnlySpan<int> INSIDE_OUT_TRANSFORMER => 
    [
         5,   4,  7,  6,
         1,   0,  3,  2,
        13,  12, 15, 14,
         9,   8, 11, 10, 
    ];

    public readonly bool IsQuarto => IS_QUARTO[this.lightHollowTallRound & 0xffff]
                                    || IS_QUARTO[(this.lightHollowTallRound >> NUM_SQUARES) & 0xffff]
                                    || IS_QUARTO[(this.lightHollowTallRound >> (NUM_SQUARES * 2)) & 0xffff]
                                    || IS_QUARTO[(this.lightHollowTallRound >> (NUM_SQUARES * 3)) & 0xffff]
                                    || IS_QUARTO[this.darkSolidShortSquare & 0xffff]
                                    || IS_QUARTO[(this.darkSolidShortSquare >> NUM_SQUARES) & 0xffff]
                                    || IS_QUARTO[(this.darkSolidShortSquare >> (NUM_SQUARES * 2)) & 0xffff]
                                    || IS_QUARTO[(this.darkSolidShortSquare >> (NUM_SQUARES * 3)) & 0xfff];

    ushort pieces = 0;
    ulong lightHollowTallRound = 0;
    ulong darkSolidShortSquare = 0;

    static Position()
    {
        const ushort HOR_MASK = 0x000f;
        const ushort VERT_MASK = 0x1111; 
        const ushort DIAG_MASK_0 = 0x1248;
        const ushort DIAG_MASK_1 = 0x8421;

        IS_QUARTO = new bool[ushort.MaxValue + 1];
        ROTATE = new ushort[ushort.MaxValue + 1];
        MIRROR = new ushort[ushort.MaxValue + 1];
        MID_FLIP = new ushort[ushort.MaxValue + 1];
        INSIDE_OUT = new ushort[ushort.MaxValue + 1];

        foreach(ushort pattern in Enumerable.Range(0, ushort.MaxValue + 1))
        {
            IS_QUARTO[pattern] = IsQuarto(pattern, HOR_MASK, BOARD_SIZE, BOARD_SIZE) 
                              || IsQuarto(pattern, VERT_MASK, 1, BOARD_SIZE)
                              || IsQuarto(pattern, DIAG_MASK_0, 0, 1)
                              || IsQuarto(pattern, DIAG_MASK_1, 0, 1);

            ROTATE[pattern] = Transform(pattern, (x, y) => (BOARD_SIZE - y - 1, x));
            MIRROR[pattern] = Transform(pattern, (x, y) => (BOARD_SIZE - x - 1, y));

            MID_FLIP[pattern] = Transform(pattern, (x, y) => { var coord = MID_FLIP_TRANSFORMER[x + y * BOARD_SIZE]; return (coord % BOARD_SIZE, coord / BOARD_SIZE); });
            INSIDE_OUT[pattern] = Transform(pattern, (x, y) => { var coord = INSIDE_OUT_TRANSFORMER[x + y * BOARD_SIZE]; return (coord % BOARD_SIZE, coord / BOARD_SIZE); });
        }

        PATTERN_ID = Enumerable.Range(0, ushort.MaxValue + 1).Select(pat => EnumerateSymmetricPatterns((ushort)pat).Min()).ToArray();

        EnumerateSymmetricPatterns(25622).ToArray();

        static bool IsQuarto(ushort pattern, ushort mask, int shiftWidth, int numShifts)
        {
            for(var i = 0; i < numShifts; i++)
            {
                if((pattern & mask) == mask)
                    return true;
                mask <<= shiftWidth;
            }
            return false;
        }

        static ushort Transform(ushort pattern, Func<int, int, (int x, int y)> transformer)
        {
            ushort transformed = 0;
            var mask = 1;
            for(var y = 0; y < BOARD_SIZE; y++)
                for(var x = 0; x < BOARD_SIZE; x++)
                {
                    (var rx, var ry) = transformer(x, y);
                    var shifts = rx + ry * BOARD_SIZE;
                    if((pattern & mask) != 0)
                        transformed |= (ushort)(1 << shifts);
                    mask <<= 1;
                }
            return transformed;
        }

        static IEnumerable<ushort> EnumerateSymmetricPatterns(ushort pattern)
        {
            for(var rotCount = 0; rotCount < 4; rotCount++)
            {
                var rotated = pattern;
                for(var i = 0; i < rotCount; i++)
                    rotated = ROTATE[rotated];

                for(var bits = 0; bits < 8; bits++)
                {
                    var transformed = rotated;

                    if((bits & 1) != 0)
                        transformed = MIRROR[transformed];
                    
                    if((bits & (1 << 1)) != 0)
                        transformed = MID_FLIP[transformed];

                    if((bits & (1 << 2)) != 0)
                        transformed = INSIDE_OUT[transformed];

                    yield return transformed;
                }
            }
        }
    }

    public Position() { }

    // for debug
    public Position(ulong lightHollowTallRound, ulong darkSolidShortSquare, ushort pieces)
    {
        this.lightHollowTallRound = lightHollowTallRound;
        this.darkSolidShortSquare = darkSolidShortSquare;
        this.pieces = pieces;
    }

    public static bool operator==(Position lhs, Position rhs) => lhs.pieces == rhs.pieces && lhs.lightHollowTallRound == rhs.lightHollowTallRound && lhs.darkSolidShortSquare == rhs.darkSolidShortSquare;
    public static bool operator!=(Position lhs, Position rhs) => !(lhs == rhs);

    public override readonly bool Equals([NotNullWhen(true)] object? obj)
    {
        if(obj is null)
            return false;

        if(obj is not Position pos)
            return false;

        return this == pos;
    }

    public override readonly int GetHashCode()
    {
        GetCanonicalPosition(out var canPos);
        return canPos.GetHashCode();
    }

    public void Update(PieceProperty piece, int coord)
    {
        this.pieces |= (ushort)(1 << coord);

        this.lightHollowTallRound |= (ulong)(piece & PieceProperty.Color) << coord;
        this.lightHollowTallRound |= (ulong)(piece & PieceProperty.Hollow) << (coord + NUM_SQUARES - 1);
        this.lightHollowTallRound |= (ulong)(piece & PieceProperty.Height) << (coord + NUM_SQUARES * 2 - 2);
        this.lightHollowTallRound |= (ulong)(piece & PieceProperty.Shape) << (coord + NUM_SQUARES * 3 - 3);

        piece = (~piece) & PieceProperty.Mask;
        this.darkSolidShortSquare |= (ulong)(piece & PieceProperty.Color) << coord;
        this.darkSolidShortSquare |= (ulong)(piece & PieceProperty.Hollow) << (coord + NUM_SQUARES - 1);
        this.darkSolidShortSquare |= (ulong)(piece & PieceProperty.Height) << (coord + NUM_SQUARES * 2 - 2);
        this.darkSolidShortSquare |= (ulong)(piece & PieceProperty.Shape) << (coord + NUM_SQUARES * 3 - 3);
    }

    public void Undo(PieceProperty piece, int coord)
    {
        this.pieces ^= (ushort)(1 << coord);

        this.lightHollowTallRound ^= (ulong)(piece & PieceProperty.Color) << coord;
        this.lightHollowTallRound ^= (ulong)(piece & PieceProperty.Hollow) << (coord + NUM_SQUARES - 1);
        this.lightHollowTallRound ^= (ulong)(piece & PieceProperty.Height) << (coord + NUM_SQUARES * 2 - 2);
        this.lightHollowTallRound ^= (ulong)(piece & PieceProperty.Shape) << (coord + NUM_SQUARES * 3 - 3);

        piece = (~piece) & PieceProperty.Mask;
        this.darkSolidShortSquare ^= (ulong)(piece & PieceProperty.Color) << coord;
        this.darkSolidShortSquare ^= (ulong)(piece & PieceProperty.Hollow) << (coord + NUM_SQUARES - 1);
        this.darkSolidShortSquare ^= (ulong)(piece & PieceProperty.Height) << (coord + NUM_SQUARES * 2 - 2);
        this.darkSolidShortSquare ^= (ulong)(piece & PieceProperty.Shape) << (coord + NUM_SQUARES * 3 - 3);
    }

    public readonly CanonicalPosition GetCanonicalPosition()
    {
        GetCanonicalPosition(out var canPos);
        return canPos;
    }

    public readonly void GetCanonicalPosition(out CanonicalPosition canonicalPos)
    {
        const int MASK = 0xffff;

        Span<ushort> patternIDs =
        [
            ushort.MaxValue,
            PATTERN_ID[this.lightHollowTallRound & MASK],
            PATTERN_ID[(this.lightHollowTallRound >> NUM_SQUARES) & MASK],
            PATTERN_ID[(this.lightHollowTallRound >> (NUM_SQUARES * 2)) & MASK],
            PATTERN_ID[(this.lightHollowTallRound >> (NUM_SQUARES * 3)) & MASK],
            ushort.MaxValue,
            PATTERN_ID[this.darkSolidShortSquare & MASK],
            PATTERN_ID[(this.darkSolidShortSquare >> NUM_SQUARES) & MASK],
            PATTERN_ID[(this.darkSolidShortSquare >> (NUM_SQUARES * 2)) & MASK],
            PATTERN_ID[(this.darkSolidShortSquare >> (NUM_SQUARES * 3)) & MASK]
        ];

        for(var i = 1; i <= 4; i++)
        {
            var j = i + 5;
            if(patternIDs[i] < patternIDs[j])
                (patternIDs[i], patternIDs[j]) = (patternIDs[j], patternIDs[i]); 
        }

        // insertion sort
        for(var i = 2; i <= 4; i++)
        {
            var tmp = (patternIDs[i], patternIDs[i + 5]);
            if(patternIDs[i - 1] < tmp.Item1 || (patternIDs[i - 1] == tmp.Item1 && patternIDs[i + 4] < tmp.Item2))
            {
                var j = i;
                do
                {
                    (patternIDs[j], patternIDs[j + 5]) = (patternIDs[j - 1], patternIDs[j + 4]);
                    j--;
                } while (patternIDs[j - 1] < tmp.Item1 || (patternIDs[j - 1] == tmp.Item1 && patternIDs[j + 4] < tmp.Item2));
                (patternIDs[j], patternIDs[j + 5]) = tmp;
            }
        }

        canonicalPos[0] = PATTERN_ID[this.pieces];
        for(var i = 0; i < 4; i++)
            (canonicalPos[i + 1], canonicalPos[i + 5]) = (patternIDs[i + 1], patternIDs[i + 6]);
    }

    public void Rotate() => Transform(ROTATE);
    public void Mirror() => Transform(MIRROR);
    public void ApplyMidFlip() => Transform(MID_FLIP);
    public void ApplyInsideOut() => Transform(INSIDE_OUT);

    public readonly bool SymmetricalEquals(ref Position pos)
    {
        foreach(var equivPos in EnumerateEquivalentPositions())
            if(equivPos == pos)
                return true;
        return false;
    }

    public readonly IEnumerable<Position> EnumerateEquivalentPositions()
    {
        const ulong MASK = 0xffff;

        int[] permutation = [0, 1, 2, 3];
        do
        {
            var propReorderedPos = new Position { pieces = this.pieces};
            for (var i = 0; i < 4; i++)
            {
                propReorderedPos.lightHollowTallRound |= ((this.lightHollowTallRound >> NUM_SQUARES * permutation[i]) & MASK) << NUM_SQUARES * i;
                propReorderedPos.darkSolidShortSquare |= ((this.darkSolidShortSquare >> NUM_SQUARES * permutation[i]) & MASK) << NUM_SQUARES * i;
            }

            Debug.Assert(BitOperations.PopCount(propReorderedPos.lightHollowTallRound) == BitOperations.PopCount(this.lightHollowTallRound));
            Debug.Assert(BitOperations.PopCount(propReorderedPos.darkSolidShortSquare) == BitOperations.PopCount(this.darkSolidShortSquare));

            for(var bits0 = 0; bits0 < 16; bits0++)
            {
                var propFlippedPos = propReorderedPos;
                for(var i = 0; i < 4; i++)
                {
                    var mask = 1 << i;
                    if((bits0 & mask) != 0)
                    {
                        var extractor = MASK << (NUM_SQUARES * i);
                        var remover = ~extractor;
                        var tmp0 = propFlippedPos.lightHollowTallRound & extractor;
                        var tmp1 = propFlippedPos.darkSolidShortSquare & extractor;
                        propFlippedPos.lightHollowTallRound &= remover;
                        propFlippedPos.lightHollowTallRound |= tmp0;
                        propFlippedPos.darkSolidShortSquare &= remover;
                        propFlippedPos.darkSolidShortSquare |= tmp1;
                    }
                }

                for(var rotCount = 0; rotCount < 4; rotCount++)
                {
                    var rotatedPos = propFlippedPos;
                    for(var i = 0; i < rotCount; i++)
                        rotatedPos.Rotate();

                    var transformedPos = rotatedPos;
                    for(var bits1 = 0; bits1 < 8; bits1++)
                    {
                        if((bits1 & 1) != 0)
                            transformedPos.Mirror();

                        if((bits1 & (1 << 1)) != 0)
                            transformedPos.ApplyMidFlip();

                        if((bits1 & (1 << 2)) != 0)
                            transformedPos.ApplyInsideOut();

                        yield return transformedPos;
                    }
                }
            }
        } while (Permutation.Next(permutation.AsSpan()));
    }

    public override readonly string ToString()
    {
        var sb = new StringBuilder();
        Print(this.lightHollowTallRound, ["Light", "Hollow", "Tall", "Round"]);
        sb.AppendLine();
        Print(this.darkSolidShortSquare, ["Dark", "Solid", "Short", "Square"]);
        return sb.ToString();

        void Print(ulong patterns, string[] labels)
        {
            for(var y = 0; y < BOARD_SIZE; y++)
            {
                if(y == 0)
                {
                    for(var i = 0; i < 4; i++)
                        sb.Append(labels[i]).Append(':').Append("\t\t");
                    sb.AppendLine();
                }

                for(var i = 0; i < 4; i++)
                {
                    var mask = 1UL << ((NUM_SQUARES * i) + BOARD_SIZE * y);
                    for(var x = 0; x < BOARD_SIZE; x++)
                    {
                        if((patterns & mask) != 0)
                            sb.Append('*');
                        else
                            sb.Append('-');
                        sb.Append(' ');
                        mask <<= 1;
                    }
                    sb.Append('\t');
                }
                sb.AppendLine();
            }
        }
    }

    void Transform(ushort[] transformer)
    {
        this.pieces = transformer[this.pieces];
        var patterns0 = 0UL;
        var patterns1 = 0UL;
        for(var i = 0; i < 4; i++)
        {
            var pattern0 = (ulong)transformer[(this.lightHollowTallRound >> (NUM_SQUARES * i)) & 0xffff];
            var pattern1 = (ulong)transformer[(this.darkSolidShortSquare >> (NUM_SQUARES * i)) & 0xffff];
            patterns0 |= pattern0 << (NUM_SQUARES * i);
            patterns1 |= pattern1 << (NUM_SQUARES * i);
        }
        this.lightHollowTallRound = patterns0;
        this.darkSolidShortSquare = patterns1;
    }
}