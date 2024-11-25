using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Quarto.Game;

namespace Quarto;

static class Program
{
    static void Main()
    {
        var pos0 = new Position(11931197164582504599, 6344407511907005704, 64927);
        var pos1 = new Position(11931197164589844631, 6344407511899665672, 64927);
        pos0.GetCanonicalPosition();
        pos1.GetCanonicalPosition();
        //CanonizeTest.TestMain(10, 10);
        // for(var i = 0; i < 10000; i++)
        // {
        //     Console.WriteLine(i);
        //     var pos = new Position();
        //     var pieces = new PieceList();
        //     var empties = new LinkedList16();

        //     var turnCount = 0;
        //     while(!pos.IsQuarto && pieces.Count != 0)
        //     {
        //         Console.WriteLine($"Turn {turnCount++}\n");

        //         var pieceIdx = Random.Shared.Next(pieces.Count);
        //         PieceProperty piece = pieces.GetNext();
        //         var count = 1;
        //         while(count <= pieceIdx)
        //         {
        //             piece = pieces.GetNext(piece);
        //             count++;
        //         }
        //         pieces.Remove(piece);

        //         var emptyIdx = Random.Shared.Next(empties.Count);
        //         var coord = empties.GetNext();
        //         count = 1;
        //         while(count <= emptyIdx)
        //         {
        //             coord = empties.GetNext(coord);
        //             count++;
        //         }
        //         empties.Remove(coord);

        //         Test(pos);

        //         Console.WriteLine($"Put {Convert.ToString((byte)piece, 2).PadLeft(4, '0')} at {coord}\n");

        //         pos.Update(piece, coord);

        //         Console.WriteLine(pos);
        //     }
        // }
    }

    static void Test(Position pos)
    {
        pos.GetCanonicalPosition(out var canPos);

        //Console.WriteLine(pos);

        foreach(var equivPos in pos.EnumerateEquivalentPositions())
        {
            equivPos.GetCanonicalPosition(out var equivCanPos);
            Debug.Assert(equivCanPos == canPos);
        }
    }
}

