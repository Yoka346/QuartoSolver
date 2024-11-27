using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic;
using Quarto.Search;

namespace Quarto.Game;

static class CanonizeTest
{
    static readonly Dictionary<CanonicalPosition, Position> TT = [];
    static ulong NodeCount;

    public static void CollisionTest(int maxDepth, int initialMoveCount)
    {
        NodeCount = 0UL;
        (var pos, var pieces, var empties) = CreateInitialPosition(initialMoveCount);
        for(var depth = 1; depth <= maxDepth; depth++)
        {
            Console.WriteLine($"depth: {depth}");
            TT.Clear();
            Search(ref pos, ref pieces, ref empties, depth);
            Console.WriteLine($"{NodeCount} nodes");
            Console.WriteLine($"{CanonicalPosition.CollisionCount} collisions");
        }
    }

    public static void HitTest(int initialMoveCount)
    {
        for(var i = 0; i < 10000; i++)
        {
            Console.WriteLine(i);
            (var pos, var pieces, var empties) = CreateInitialPosition(initialMoveCount);

            while(!pos.IsQuarto && pieces.Count != 0)
            {
                var pieceIdx = Random.Shared.Next(pieces.Count);
                PieceProperty piece = pieces.GetNext();
                var count = 1;
                while(count <= pieceIdx)
                {
                    piece = pieces.GetNext(piece);
                    count++;
                }
                pieces.Remove(piece);

                var emptyIdx = Random.Shared.Next(empties.Count);
                var coord = empties.GetNext();
                count = 1;
                while(count <= emptyIdx)
                {
                    coord = empties.GetNext(coord);
                    count++;
                }
                empties.Remove(coord);

                Test(pos);

                pos.PieceToBePut = piece;
                pos.Update(coord);
            }
        }

        static void Test(Position pos)
        {
            pos.GetCanonicalPosition(out var canPos);
            Debug.Assert(pos.GetCanonicalPosition() == canPos);

            foreach(var equivPos in pos.GetEquivalentPositions())
            {
                equivPos.GetCanonicalPosition(out var equivCanPos);
                Debug.Assert(equivPos.GetCanonicalPosition() == equivCanPos);
                if(equivCanPos != canPos)
                {
                    Console.WriteLine($"{pos}\nPiece: {pos.PieceToBePut}\n");
                    Console.WriteLine($"{equivPos}\nPiece: {equivPos.PieceToBePut}\n");
                    Console.WriteLine(canPos);
                    Console.WriteLine(equivCanPos);
                    Debug.Assert(false);
                }
            }
        }
    }

    static (Position, PieceList, LinkedList16) CreateInitialPosition(int moveCount)
    {
        var pos = new Position();
        var pieces = new PieceList();
        var empties = new LinkedList16();
        for(var i = 0; i < moveCount && !pos.IsQuarto; i++)
        {
            var pieceIdx = Random.Shared.Next(pieces.Count);
            PieceProperty piece = pieces.GetNext();
            var count = 1;
            while(count <= pieceIdx)
            {
                piece = pieces.GetNext(piece);
                count++;
            }
            pieces.Remove(piece);
            pos.PieceToBePut = piece;

            var emptyIdx = Random.Shared.Next(empties.Count);
            var coord = empties.GetNext();
            count = 1;
            while(count <= emptyIdx)
            {
                coord = empties.GetNext(coord);
                count++;
            }
            empties.Remove(coord);

            pos.Update(coord);
        }
        return (pos, pieces, empties);
    }

    static void Search(ref Position pos, ref PieceList pieces, ref LinkedList16 empties, int depth)
    {
        if(depth == 0)
        {
            pos.GetCanonicalPosition(out var canPos);
            if(TT.TryGetValue(canPos, out var entry))
            {
                var entryCanPos = entry.GetCanonicalPosition();
                Debug.Assert(entry.SymmetricalEquals(ref pos));
            }
            else
                TT[canPos] = pos;
            NodeCount++;
            return;
        }

        var piece = PieceProperty.Default;
        var prevPiece = piece;
        while((piece = pieces.GetNext(piece)) != PieceProperty.Default)
        {
            pieces.Remove(piece);
            pos.PieceToBePut = piece;
            var coord = -1;
            var prevCoord = coord;
            while((coord = empties.GetNext(coord)) != -1)
            {
                empties.Remove(coord);
                pos.Update(coord);

                Search(ref pos, ref pieces, ref empties, depth - 1);

                pos.Undo(piece, coord);
                empties.AddAfter(prevCoord, coord);
                prevCoord = coord;
            }
            pieces.AddAfter(prevPiece, piece);
            prevPiece = piece;
        }
    }
}

