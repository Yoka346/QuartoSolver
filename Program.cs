using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Quarto.Game;
using Quarto.Search;

namespace Quarto;

static class Program
{
    static void Main(string[] args)
    {
        const ulong DEFAULT_TT_SIZE = 8UL * 1024 * 1024 * 1024;

        var searcher = new Searcher(args.Length == 0 ? DEFAULT_TT_SIZE : ulong.Parse(args[0]) * 1024 * 1024);
        searcher.SetRoot(new Position(), new PieceList(), new LinkedList16());

        var task = Task.Run(() => searcher.Search(32));

        while(!task.IsCompleted)
        {
            Console.WriteLine($"{searcher.NodeCount}[nodes]");
            Thread.Sleep(10000);
        }

        var res = task.Result;
        Console.WriteLine($"Score: {res.EvalScore}");
        Console.WriteLine($"NodeCount: {res.NodeCount}");
        Console.WriteLine($"Ellapsed: {res.EllapsedMs}[ms]");
    }
}

