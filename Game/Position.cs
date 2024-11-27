namespace Quarto.Game;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
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
    Mask = 0xf,
    Null = 1 << 4
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
    const int LEN = 5;

    public static int Length => LEN;
    
    static readonly ulong[][] HASH_RAND = new ulong[LEN * 2][]; 

    public uint this[int idx] 
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

    fixed uint values[LEN];

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
            && lhs.values[4] == rhs.values[4];
    }

    public static bool operator!=(CanonicalPosition lhs, CanonicalPosition rhs) => !(lhs == rhs);

    public readonly ulong CalcHashCode()
    {
        if(!Sse42.IsSupported && !Crc32.IsSupported)
        {
            fixed(uint* v = this.values)
            {
                var values = (ushort*)v;

                var hashCode = 0UL;
                for(var i = 0; i < HASH_RAND.Length; i++)
                    hashCode ^= HASH_RAND[i][values[i] & 0xffff];

                return hashCode;
            }
        }

        fixed(uint* v = this.values)
        {
            var values = (ulong*)v;
            var crc = 0UL;
            for(var i = 1; i < LEN / 2; i++)
                crc = ComputeCrc32(crc, values[i]);
            crc = ComputeCrc32(crc, this.values[LEN - 1]);

            return crc;
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
    => $"{values[0]},  {values[1]},  {values[2]},  {values[3]},  {values[4]}";

    static ulong ComputeCrc32(ulong crc, ulong data)
    {
        if(Crc32.IsSupported)
        {
            if(Crc32.Arm64.IsSupported)
                return Crc32.Arm64.ComputeCrc32((uint)crc, data);
            else
                return Crc32.ComputeCrc32(Crc32.ComputeCrc32((uint)crc, (uint)data), (uint)(data >> 32));
        }

        if (Sse42.X64.IsSupported)
                return Sse42.X64.Crc32(crc, data);
            
        return Sse42.Crc32(Sse42.Crc32((uint)crc, (uint)data), (uint)(data >> 32));
    }
}

public unsafe struct Position
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

    public readonly bool IsQuarto => IS_QUARTO[this.lightHollowTallRound[0]]
                                    || IS_QUARTO[this.lightHollowTallRound[1]]
                                    || IS_QUARTO[this.lightHollowTallRound[2]]
                                    || IS_QUARTO[this.lightHollowTallRound[3]]
                                    || IS_QUARTO[this.darkSolidShortSquare[0]]
                                    || IS_QUARTO[this.darkSolidShortSquare[1]]
                                    || IS_QUARTO[this.darkSolidShortSquare[2]]
                                    || IS_QUARTO[this.darkSolidShortSquare[3]];

    public PieceProperty PieceToBePut { get; set; } = PieceProperty.Null;

    fixed ushort lightHollowTallRound[4];
    fixed ushort darkSolidShortSquare[4];

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

    public Position() 
    {
        for(var i = 0; i < 4; i++)
            this.lightHollowTallRound[i] = this.darkSolidShortSquare[i] = 0;
    }

    public static bool operator==(Position lhs, Position rhs) => lhs.PieceToBePut == rhs.PieceToBePut 
                                                              && AreEqual(lhs.lightHollowTallRound, rhs.lightHollowTallRound) 
                                                              && AreEqual(lhs.darkSolidShortSquare, rhs.darkSolidShortSquare);

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

    public void Update(int coord)
    {
        var piece = this.PieceToBePut;
        this.lightHollowTallRound[0] |= (ushort)((int)(piece & PieceProperty.Color) << coord);
        this.lightHollowTallRound[1] |= (ushort)(((int)(piece & PieceProperty.Hollow) << coord) >> 1);
        this.lightHollowTallRound[2] |= (ushort)(((int)(piece & PieceProperty.Height) << coord) >> 2);
        this.lightHollowTallRound[3] |= (ushort)(((int)(piece & PieceProperty.Shape) << coord) >> 3);

        piece = (~piece) & PieceProperty.Mask;
        this.darkSolidShortSquare[0] |= (ushort)((int)(piece & PieceProperty.Color) << coord);
        this.darkSolidShortSquare[1] |= (ushort)(((int)(piece & PieceProperty.Hollow) << coord) >> 1);
        this.darkSolidShortSquare[2] |= (ushort)(((int)(piece & PieceProperty.Height) << coord) >> 2);
        this.darkSolidShortSquare[3] |= (ushort)(((int)(piece & PieceProperty.Shape) << coord) >> 3);
    }

    public void Undo(PieceProperty piece, int coord)
    {
        this.lightHollowTallRound[0] ^= (ushort)((int)(piece & PieceProperty.Color) << coord);
        this.lightHollowTallRound[1] ^= (ushort)(((int)(piece & PieceProperty.Hollow) << coord) >> 1);
        this.lightHollowTallRound[2] ^= (ushort)(((int)(piece & PieceProperty.Height) << coord) >> 2);
        this.lightHollowTallRound[3] ^= (ushort)(((int)(piece & PieceProperty.Shape) << coord) >> 3);

        piece = (~piece) & PieceProperty.Mask;
        this.darkSolidShortSquare[0] ^= (ushort)((int)(piece & PieceProperty.Color) << coord);
        this.darkSolidShortSquare[1] ^= (ushort)(((int)(piece & PieceProperty.Hollow) << coord) >> 1);
        this.darkSolidShortSquare[2] ^= (ushort)(((int)(piece & PieceProperty.Height) << coord) >> 2);
        this.darkSolidShortSquare[3] ^= (ushort)(((int)(piece & PieceProperty.Shape) << coord) >> 3);
    }

    public readonly CanonicalPosition GetCanonicalPosition()
    {
        GetCanonicalPosition(out var canPos);
        return canPos;
    }

    public readonly void GetCanonicalPosition(out CanonicalPosition canonicalPos)
    {
        Span<ushort> rotatedLHTR = [this.lightHollowTallRound[0], this.lightHollowTallRound[1], this.lightHollowTallRound[2], this.lightHollowTallRound[3]];
        Span<ushort> rotatedDSSS = [this.darkSolidShortSquare[0], this.darkSolidShortSquare[1], this.darkSolidShortSquare[2], this.darkSolidShortSquare[3]];
        Span<ushort> transformedLHTR = stackalloc ushort[4];
        Span<ushort> transformedDSSS = stackalloc ushort[4];
        Span<uint> encodedPos = stackalloc uint[5];
        encodedPos[0] = uint.MaxValue;
        Span<uint> minEncodedPos = [uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue];
        var minCanonicalPiece = uint.MaxValue;

        var pieceToBePut = (uint)this.PieceToBePut;

        for(var rotCount = 0; rotCount < 4; rotCount++)
        {
            for(var i = 0; i < rotatedLHTR.Length; i++)
                rotatedLHTR[i] = ROTATE[rotatedLHTR[i]];

            for(var i = 0; i < rotatedDSSS.Length; i++)
                rotatedDSSS[i] = ROTATE[rotatedDSSS[i]];

            for(var bits = 0; bits < 8; bits++)
            {
                rotatedLHTR.CopyTo(transformedLHTR);
                rotatedDSSS.CopyTo(transformedDSSS);

                if((bits & 1) != 0)
                {
                    Transform(transformedLHTR, MIRROR);
                    Transform(transformedDSSS, MIRROR);
                }

                if((bits & (1 << 1)) != 0)
                {
                    Transform(transformedLHTR, MID_FLIP);
                    Transform(transformedDSSS, MID_FLIP);
                }

                if((bits & (1 << 2)) != 0)
                {
                    Transform(transformedLHTR, INSIDE_OUT);
                    Transform(transformedDSSS, INSIDE_OUT);
                }

                var propFlipMask = 0u;
                for(var i = 0; i < 4; i++)
                {
                    Debug.Assert((transformedLHTR[i] == 0 && transformedDSSS[i] == 0) || (transformedLHTR[i] != transformedDSSS[i]));

                    if(transformedLHTR[i] <= transformedDSSS[i])
                        encodedPos[i + 1] = (uint)(transformedLHTR[i] << NUM_SQUARES) | transformedDSSS[i];
                    else
                    {
                        encodedPos[i + 1] = (uint)(transformedDSSS[i] << NUM_SQUARES) | transformedLHTR[i];
                        propFlipMask |= 1u << i;
                    }
                }

                var canonicalPiece = Sort(encodedPos, pieceToBePut ^ propFlipMask);
                var comp = Compare(encodedPos, minEncodedPos);
                if(comp == -1 || (comp == 0 && canonicalPiece < minCanonicalPiece))
                {
                    encodedPos.CopyTo(minEncodedPos);
                    minCanonicalPiece = canonicalPiece;
                }
            }
        }

        for(var i = 0; i < CanonicalPosition.Length - 1; i++)
            canonicalPos[i] = minEncodedPos[i + 1];
        canonicalPos[CanonicalPosition.Length - 1] = minCanonicalPiece;

        static void Transform(Span<ushort> patterns, ushort[] table)
        {
            for(var i = 0; i < patterns.Length; i++)
                patterns[i] = table[patterns[i]];
        }

        static uint Sort(Span<uint> encodedPos, uint pieceToBePut)
        {
            var canonicalPiece = pieceToBePut;
            for(var i = 2; i < encodedPos.Length; i++)
            {
                var tmp = encodedPos[i];
                if(encodedPos[i - 1] < tmp)
                {
                    var j = i;
                    do
                    {
                        encodedPos[j] = encodedPos[j - 1];
                        var mask = ((canonicalPiece >> 1) ^ canonicalPiece) & (1u << (j - 2));
                        mask ^= mask << 1;
                        canonicalPiece ^= mask;
                        j--;
                    } while (encodedPos[j - 1] < tmp);

                    encodedPos[j] = tmp;
                } 
            }
            return canonicalPiece;
        }

        static int Compare(Span<uint> encodedPos0, Span<uint> encodedPos1)
        {
            for(var i = 1; i < 5; i++)
            {
                if(encodedPos0[i] == encodedPos1[i])
                    continue;
                return (encodedPos0[i] < encodedPos1[i]) ? -1 : 1;
            }
            return 0;
        }
    }

    public void Rotate() => Transform(ROTATE);
    public void Mirror() => Transform(MIRROR);
    public void ApplyMidFlip() => Transform(MID_FLIP);
    public void ApplyInsideOut() => Transform(INSIDE_OUT);

    public readonly bool SymmetricalEquals(ref Position pos)
    {
        foreach(var equivPos in GetEquivalentPositions())
            if(equivPos == pos)
                return true;
        return false;
    }

    public readonly unsafe Position[] GetEquivalentPositions()
    {
        var equivPositions = new Position[24 * 16 * 32];
        var count = 0;

        int[] permutation = [0, 1, 2, 3];
        do
        {
            var propReorderedPos = new Position { PieceToBePut = 0u };
            for (var i = 0; i < 4; i++)
            {
                propReorderedPos.lightHollowTallRound[i] = this.lightHollowTallRound[permutation[i]];
                propReorderedPos.darkSolidShortSquare[i] = this.darkSolidShortSquare[permutation[i]];
                propReorderedPos.PieceToBePut |= (PieceProperty)((((uint)this.PieceToBePut >> permutation[i]) & 1) << i);
            }

            if(this.PieceToBePut == PieceProperty.Null)
                propReorderedPos.PieceToBePut = PieceProperty.Null;

            for(var bits0 = 0; bits0 < 16; bits0++)
            {
                var propFlippedPos = propReorderedPos;
                for(var i = 0; i < 4; i++)
                {
                    var mask = 1 << i;
                    if((bits0 & mask) != 0)
                    {
                        (propFlippedPos.lightHollowTallRound[i], propFlippedPos.darkSolidShortSquare[i]) = (propFlippedPos.darkSolidShortSquare[i], propFlippedPos.lightHollowTallRound[i]);
                        propFlippedPos.PieceToBePut ^= (PieceProperty)mask;
                    }
                }

                if(this.PieceToBePut == PieceProperty.Null)
                    propFlippedPos.PieceToBePut = PieceProperty.Null;

                for(var rotCount = 0; rotCount < 4; rotCount++)
                {
                    var rotatedPos = propFlippedPos;
                    for(var i = 0; i < rotCount; i++)
                        rotatedPos.Rotate();

                    for(var bits1 = 0; bits1 < 8; bits1++)
                    {
                        var transformedPos = rotatedPos;

                        if((bits1 & 1) != 0)
                            transformedPos.Mirror();

                        if((bits1 & (1 << 1)) != 0)
                            transformedPos.ApplyMidFlip();

                        if((bits1 & (1 << 2)) != 0)
                            transformedPos.ApplyInsideOut();

                        equivPositions[count++] = transformedPos;
                    }
                }
            }
        } while (Permutation.Next(permutation.AsSpan()));

        return equivPositions;
    }

    public override readonly string ToString()
    {
        var sb = new StringBuilder();

        fixed(ushort* lhtr = this.lightHollowTallRound)
            Print(lhtr, ["Light", "Hollow", "Tall", "Round"]);

        sb.AppendLine();

        fixed(ushort* dsss = this.darkSolidShortSquare)
            Print(dsss, ["Dark", "Solid", "Short", "Square"]);

        return sb.ToString();

        void Print(ushort* patterns, string[] labels)
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
                    var mask = 1 << (BOARD_SIZE * y);
                    for(var x = 0; x < BOARD_SIZE; x++)
                    {
                        if((patterns[i] & mask) != 0)
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
        for(var i = 0; i < 4; i++)
        {
            this.lightHollowTallRound[i] = transformer[this.lightHollowTallRound[i]];
            this.darkSolidShortSquare[i] = transformer[this.darkSolidShortSquare[i]];
        }
    }

    static bool AreEqual(ushort* patterns0, ushort* patterns1)
    {
        return patterns0[0] == patterns1[0]
            && patterns0[1] == patterns1[1]
            && patterns0[2] == patterns1[2]
            && patterns0[3] == patterns1[3];
    }
}