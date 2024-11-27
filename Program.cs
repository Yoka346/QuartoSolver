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
    static void Main()
    {
        var searcher = new Searcher();
        searcher.SetRoot(new Position(), new PieceList(), new LinkedList16());
        SearchResult res = new();

        var task = Task.Run(() => 
        {
            res = searcher.Search();
        });

        while(!task.IsCompleted)
        {
            Console.WriteLine($"{searcher.NodeCount}[nodes]");
            Thread.Sleep(10000);
        }

        Console.WriteLine($"Score: {res.EvalScore}");
        Console.WriteLine($"NodeCount: {res.NodeCount}");
        Console.WriteLine($"Ellapsed: {res.EllapsedMs}[ms]");
    }
}

