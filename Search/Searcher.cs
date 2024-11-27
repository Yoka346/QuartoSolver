using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Runtime.Serialization;
using Quarto.Game;

namespace Quarto.Search;

public readonly struct SearchResult 
{
    public Move BestMove { get; }
    public int EvalScore { get; }
    public int Depth { get; }
    public long NodeCount { get; }
    public int EllapsedMs { get; }

    public SearchResult(Move bestMove, int evalScore, int depth, long nodeCount, int ellpasedMs)
    {
        this.BestMove = bestMove;
        this.EvalScore = evalScore;
        this.Depth = depth;
        this.NodeCount = nodeCount;
        this.EllapsedMs = ellpasedMs;
    }
}

class Searcher
{
    const int SCORE_INF = short.MaxValue;
    const int SCORE_MATE = SCORE_INF;
    const int SCORE_INVALID = int.MaxValue;
    const int SHALLOW_DEPTH = 5;

    public long NodeCount { get; private set; }

    Position rootPos;
    PieceList rootPieces;
    LinkedList16 rootEmpties;
    readonly Dictionary<CanonicalPosition, TTEntry> tt;

    public Searcher()
    {
        this.tt = [];
    }

    public void SetRoot(Position pos, PieceList pieces, LinkedList16 empties)
    {
        this.rootPos = pos;
        this.rootPieces = pieces;
        this.rootEmpties = empties;
        this.tt.Clear();
    }

    public SearchResult Search()
    {
        Move bestMove;
        this.NodeCount = 0L;
        var startMs = Environment.TickCount;
        (var pos, var pieces, var empties) = (this.rootPos, this.rootPieces, this.rootEmpties);
        var score = SearchChoiceNode(ref pos, ref pieces, ref empties, 1, -SCORE_INF, SCORE_INF);
        var endMs = Environment.TickCount;
        return new SearchResult(default, score, 0, this.NodeCount, endMs - startMs);
    }

    public int SearchChoiceNode(ref Position pos, ref PieceList pieces, ref LinkedList16 empties, int sideToMove, int alpha, int beta)
    {
        if(empties.Count == 0)
            return 0;

        if(pos.IsQuarto)
            return -SCORE_MATE;

        var prevChoice = pos.PieceToBePut;
        var piece = PieceProperty.Default;
        var prevPiece = piece;
        while((piece = pieces.GetNext(piece)) != PieceProperty.Default)
        {
            pieces.Remove(piece);
            pos.PieceToBePut = piece;
            this.NodeCount++;

            alpha = Math.Max(alpha, -SearchPutNode(ref pos, ref pieces, ref empties, -sideToMove, -beta, -alpha));

            pieces.AddAfter(prevPiece, piece);
            prevPiece = piece;

            if(alpha >= beta)
            {
                pos.PieceToBePut = prevChoice;
                return alpha;
            }
        }

        pos.PieceToBePut = prevChoice;
        return alpha;
    }

    public int SearchPutNode(ref Position pos, ref PieceList pieces, ref LinkedList16 empties, int sideToMove, int alpha, int beta)
    {
        if(empties.Count > SHALLOW_DEPTH)
            return SearchPutNodeWithTT(ref pos, ref pieces, ref empties, sideToMove, alpha, beta);
        else
            return SearchPutNodeWithoutTT(ref pos, ref pieces, ref empties, sideToMove, alpha, beta);
    }

    public int SearchPutNodeWithTT(ref Position pos, ref PieceList pieces, ref LinkedList16 empties, int sideToMove, int alpha, int beta)
    {
        var canPos = pos.GetCanonicalPosition();
        if(this.tt.TryGetValue(canPos, out var entry))
        {
            var lower = entry.Lower;
            var upper = entry.Upper;
            var ret = SCORE_INVALID;
            if(alpha >= upper)
                ret = upper;
            else if(beta <= lower)
                ret = lower;
            else if(lower == upper)
                ret = lower;

            if(ret != SCORE_INVALID)
                return ret;

            alpha = Math.Max(alpha, lower);
            beta = Math.Min(beta, upper);
        }

        int maxScore = -SCORE_INF, score, a = alpha;
        var coord = -1;
        var prevCoord = coord;
        while((coord = empties.GetNext(coord)) != -1)
        {
            empties.Remove(coord);
            pos.Update(coord);
            this.NodeCount++;

            score = SearchChoiceNode(ref pos, ref pieces, ref empties, sideToMove, a, beta);

            pos.Undo(pos.PieceToBePut, coord);
            empties.AddAfter(prevCoord, coord);
            prevCoord = coord;

            if(score >= beta)
            {
                this.tt[canPos] = new TTEntry
                {
                    Lower = (short)score,
                    Upper = SCORE_INF
                };
                return score;
            }

            if(score > a)
                a = score;

            if(score > maxScore)
            {
                maxScore = score;
                a = Math.Max(alpha, score);
            }
        }

        if(maxScore >= alpha)
        {
            this.tt[canPos] = new TTEntry
            {
                Lower = (short)maxScore,
                Upper = (short)maxScore
            };
        }
        else
        {
            this.tt[canPos] = new TTEntry
            {
                Lower = -SCORE_INF,
                Upper = (short)maxScore
            };
        }

        return maxScore;
    }

    public int SearchPutNodeWithoutTT(ref Position pos, ref PieceList pieces, ref LinkedList16 empties, int sideToMove, int alpha, int beta)
    {
        int maxScore = -SCORE_INF, score, a = alpha;
        var coord = -1;
        var prevCoord = coord;
        while((coord = empties.GetNext(coord)) != -1)
        {
            empties.Remove(coord);
            pos.Update(coord);

            score = SearchChoiceNode(ref pos, ref pieces, ref empties, sideToMove, a, beta);

            pos.Undo(pos.PieceToBePut, coord);
            empties.AddAfter(prevCoord, coord);
            prevCoord = coord;

            if(score >= beta)
                return score;

            if(score > a)
                a = score;

            if(score > maxScore)
            {
                maxScore = score;
                a = Math.Max(alpha, score);
            }
        }

        return maxScore;
    }
}