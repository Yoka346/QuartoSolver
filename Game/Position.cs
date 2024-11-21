namespace Game;

using System;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.Intrinsics;
using static QuartoConstants;

public static class QuartoConstants
{
    public const int BOARD_SIZE = 4;
    public const int NUM_SQUARES = BOARD_SIZE * BOARD_SIZE;
    public const int NUM_PIECE_PROPERTIES = 8;
}

public enum PieceState : byte
{
    Light = 1,
    Hollow = 1 << 1,
    Tall = 1 << 2,
    Round = 1 << 3,

    Dark = 1 << 4,
    Solid = 1 << 5,
    Short = 1 << 6,
    Square = 1 << 7
}

public readonly struct Move(int coord, PieceState piece)
{
    public readonly int Coord => COORD;
    public readonly PieceState Piece => PIECE;

    readonly int COORD = coord;
    readonly PieceState PIECE = piece;
}

public unsafe struct CanonicalPosition
{
    const int LEN = 9;

    public static int Length => LEN;

    public readonly ushort this[int idx] 
    {
        get
        {
            Debug.Assert(idx >= 0 && idx < LEN);
            return values[idx];
        }
    }

    fixed ushort values[LEN];

    public CanonicalPosition(Span<ushort> values)
    {
        Debug.Assert(values.Length == LEN);

        for(var i = 0; i < LEN; i++)
            this.values[i] = values[i];
    }
}

public struct Position
{
    static readonly bool[] IS_QUARTO;

    static readonly ushort[] ROTATE;
    static readonly ushort[] MIRROR;
    static readonly ushort[] MID_FLIP;
    static readonly ushort[] INSIDE_OUT;

    static readonly int[] POPCOUNT;

    static readonly ushort[] PATTERN_ID;

    static readonly ushort[] MIN_ORDER;


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
                                    || IS_QUARTO[(this.lightHollowTallRound >> NUM_SQUARES * 2) & 0xffff]
                                    || IS_QUARTO[(this.lightHollowTallRound >> NUM_SQUARES * 3) & 0xffff]
                                    || IS_QUARTO[this.darkSolidShortSquare & 0xffff]
                                    || IS_QUARTO[(this.darkSolidShortSquare >> NUM_SQUARES) & 0xffff]
                                    || IS_QUARTO[(this.darkSolidShortSquare >> NUM_SQUARES * 2) & 0xffff]
                                    || IS_QUARTO[(this.darkSolidShortSquare >> NUM_SQUARES * 3) & 0xfff];

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

        POPCOUNT = new int[ushort.MaxValue + 1];

        // ToDo: PATTERN_IDとMIN_ORDERの初期化
        PATTERN_ID = new ushort[ushort.MaxValue + 1];

        MIN_ORDER = new ushort[ushort.MaxValue + 1];



        for(ushort pattern = 0; pattern < IS_QUARTO.Length; pattern++)
        {
            IS_QUARTO[pattern] = IsQuarto(pattern, HOR_MASK, BOARD_SIZE, BOARD_SIZE) 
                              || IsQuarto(pattern, VERT_MASK, 1, BOARD_SIZE)
                              || IsQuarto(pattern, DIAG_MASK_0, 0, 1)
                              || IsQuarto(pattern, DIAG_MASK_1, 0, 1);

            ROTATE[pattern] = Transform(pattern, (x, y) => (BOARD_SIZE - y - 1, -x));
            MIRROR[pattern] = Transform(pattern, (x, y) => (-x, y));

            MID_FLIP[pattern] = Transform(pattern, (x, y) => { var coord = MID_FLIP_TRANSFORMER[x + y * BOARD_SIZE]; return (coord % BOARD_SIZE, coord / BOARD_SIZE); });
            INSIDE_OUT[pattern] = Transform(pattern, (x, y) => { var coord = INSIDE_OUT_TRANSFORMER[x + y * BOARD_SIZE]; return (coord % BOARD_SIZE, coord / BOARD_SIZE); });

            POPCOUNT[pattern] = (ushort)BitOperations.PopCount(pattern);
        }

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
                }
            return transformed;
        }
    }

    public Position() { }

    public void Update(ref Move move)
    {
        this.pieces |= (ushort)(1 << move.Coord);

        this.lightHollowTallRound |= (ulong)(move.Piece & PieceState.Light) << move.Coord;
        this.lightHollowTallRound |= (ulong)(move.Piece & PieceState.Hollow) << (move.Coord + NUM_SQUARES - 1);
        this.lightHollowTallRound |= (ulong)(move.Piece & PieceState.Tall) << (move.Coord + NUM_SQUARES - 2);
        this.lightHollowTallRound |= (ulong)(move.Piece & PieceState.Round) << (move.Coord + NUM_SQUARES - 3);

        this.darkSolidShortSquare |= (ulong)(move.Piece & PieceState.Dark) << (move.Coord + NUM_SQUARES - 4);
        this.darkSolidShortSquare |= (ulong)(move.Piece & PieceState.Solid) << (move.Coord + NUM_SQUARES - 5);
        this.darkSolidShortSquare |= (ulong)(move.Piece & PieceState.Short) << (move.Coord + NUM_SQUARES - 6);
        this.darkSolidShortSquare |= (ulong)(move.Piece & PieceState.Square) << (move.Coord + NUM_SQUARES - 7);
    }

    public void Undo(ref Move move)
    {
        this.pieces ^= (ushort)(1 << move.Coord);

        this.lightHollowTallRound ^= (ulong)(move.Piece & PieceState.Light) << move.Coord;
        this.lightHollowTallRound ^= (ulong)(move.Piece & PieceState.Hollow) << (move.Coord + NUM_SQUARES - 1);
        this.lightHollowTallRound ^= (ulong)(move.Piece & PieceState.Tall) << (move.Coord + NUM_SQUARES - 2);
        this.lightHollowTallRound ^= (ulong)(move.Piece & PieceState.Round) << (move.Coord + NUM_SQUARES - 3);

        this.darkSolidShortSquare ^= (ulong)(move.Piece & PieceState.Dark) << (move.Coord + NUM_SQUARES - 4);
        this.darkSolidShortSquare ^= (ulong)(move.Piece & PieceState.Solid) << (move.Coord + NUM_SQUARES - 5);
        this.darkSolidShortSquare ^= (ulong)(move.Piece & PieceState.Short) << (move.Coord + NUM_SQUARES - 6);
        this.darkSolidShortSquare ^= (ulong)(move.Piece & PieceState.Square) << (move.Coord + NUM_SQUARES - 7);
    }

    public Vector128<ushort> GetCanonicalForm()
    {
        (int rotCount, int mirrorCount, int midFlipCount, int insideOutCount) transform = (0, 0, 0, 0);
        var min = ushort.MaxValue;
        for(var rotCount = 0; rotCount < 4; rotCount++)
        {
            ushort rotated = this.pieces;
            for(var i = 0; i < rotCount; i++)
                rotated = ROTATE[rotated];

            int mr, mf, io;
            mr = mf = io = 0;
            for(var bits = 0; bits < 8; bits++)
            {
                var transformed = rotated;

                if((bits & 1) != 0)
                {
                    transformed = MIRROR[transformed];
                    mr = 1;
                }

                if((bits & (1 << 1)) != 0)
                {
                    transformed = MID_FLIP[transformed];
                    mf = 1;
                }

                if((bits & (1 << 2)) != 0)
                {
                    transformed = INSIDE_OUT[transformed];
                    io = 1;
                }
            }
        }
    }
}