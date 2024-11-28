using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Runtime.Serialization;
using Microsoft.VisualBasic;
using Quarto.Game;

namespace Quarto.Search;

public readonly struct SearchResult(Move bestMove, int evalScore, int depth, long nodeCount, int ellpasedMs)
{
    public Move BestMove { get; } = bestMove;
    public int EvalScore { get; } = evalScore;
    public int Depth { get; } = depth;
    public long NodeCount { get; } = nodeCount;
    public int EllapsedMs { get; } = ellpasedMs;
}

class Searcher(ulong ttSizeBytes)
{
    const int SCORE_MATE = sbyte.MaxValue;
    const int SCORE_INVALID = int.MaxValue;
    const int WITHOUT_TT_EMPTY_COUNT = 5;

    public long NodeCount { get; private set; }

    SearchState rootState;
    readonly TranspositionTable tt = new(ttSizeBytes);

    public void SetRoot(Position pos, PieceList pieces, LinkedList16 empties)
    {
        this.rootState = new SearchState(pos, pieces, empties);
        this.tt.Clear();
    }

    public SearchResult Search(int depth)
    {
        this.NodeCount = 0L;
        var startMs = Environment.TickCount;
        var state = this.rootState;
        var score = SearchChoiceNode(ref state, -SCORE_MATE, SCORE_MATE, depth);
        var endMs = Environment.TickCount;
        return new SearchResult(default, score, 0, this.NodeCount, endMs - startMs);
    }

    int SearchChoiceNode(ref SearchState state, int alpha, int beta, int depth)
    {
        if(state.Empties.Count == 0 || depth == 0)
            return 0;

        state.Pos.GetCanonicalPosition(out var canPos);
        (var lower, var upper) = GetScoreRange(ref state, ref canPos);

        if(upper <= alpha)
            return upper;
        
        if(lower >= beta || lower == upper)
            return lower;

        alpha = Math.Max(alpha, lower);
        beta = Math.Min(beta, upper);

        var prevChoice = state.Pos.Piece;
        var piece = PieceProperty.Default;
        var prevPiece = piece;
        while((piece = state.Pieces.GetNext(piece)) != PieceProperty.Default)
        {
            state.Pieces.Remove(piece);
            state.Pos.Piece = piece;
            this.NodeCount++;

            alpha = Math.Max(alpha, -SearchPutNode(ref state, -beta, -alpha, depth - 1));

            state.Pieces.AddAfter(prevPiece, piece);
            prevPiece = piece;

            if(alpha >= beta)
            {
                state.Pos.Piece = prevChoice;
                return alpha;
            }
        }

        state.Pos.Piece = prevChoice;
        return alpha;
    }

    int SearchPutNode(ref SearchState state, int alpha, int beta, int depth)
    {
        if(state.Empties.Count > WITHOUT_TT_EMPTY_COUNT)
            return SearchPutNodeWithTT(ref state, alpha, beta, depth);
        else
            return SearchPutNodeWithoutTT(ref state, alpha, beta, depth);
    }

    int SearchPutNodeWithTT(ref SearchState state, int alpha, int beta, int depth)
    {
        if(SearchMateInOneMove(ref state))
            return SCORE_MATE;

        if(depth == 0)
            return 0;

        state.Pos.GetCanonicalPosition(out var canPos);
        ref var entry = ref this.tt.GetEntry(ref canPos, out var hit);

        if(hit)
        {
            var lower = entry.Lower;
            var upper = entry.Upper;

            if(alpha >= upper)
                return upper;
            
            if(beta <= lower || lower == upper)
                return lower;

            alpha = Math.Max(alpha, lower);
            beta = Math.Min(beta, upper);
        }

        int maxScore = -SCORE_MATE, score, a = alpha;
        var coord = -1;
        var prevCoord = coord;
        while((coord = state.Empties.GetNext(coord)) != -1)
        {
            state.Empties.Remove(coord);
            state.Pos.Update(coord);
            this.NodeCount++;

            score = SearchChoiceNode(ref state, a, beta, depth - 1);

            state.Pos.Undo(state.Pos.Piece, coord);
            state.Empties.AddAfter(prevCoord, coord);
            prevCoord = coord;

            if(score >= beta)
            {
                TranspositionTable.SaveAt(ref entry, ref canPos, score, SCORE_MATE, depth);
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
            TranspositionTable.SaveAt(ref entry, ref canPos, maxScore, maxScore, depth);
        else
            TranspositionTable.SaveAt(ref entry, ref canPos, -SCORE_MATE, maxScore, depth);

        return maxScore;
    }

    int SearchPutNodeWithoutTT(ref SearchState state, int alpha, int beta, int depth)
    {
        if(SearchMateInOneMove(ref state))
            return SCORE_MATE;

        if(depth == 0)
            return 0;

        int maxScore = -SCORE_MATE, score, a = alpha;
        var coord = -1;
        var prevCoord = coord;
        while((coord = state.Empties.GetNext(coord)) != -1)
        {
            state.Empties.Remove(coord);
            state.Pos.Update(coord);
            this.NodeCount++;

            score = SearchChoiceNode(ref state, a, beta, depth - 1);

            state.Pos.Undo(state.Pos.Piece, coord);
            state.Empties.AddAfter(prevCoord, coord);
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

    (int lower, int upper) GetScoreRange(ref SearchState state, ref CanonicalPosition pos)
    {
        var prevChoice = state.Pos.Piece;
        (var lower, var upper) = (-SCORE_MATE, -SCORE_MATE);
        var piece = PieceProperty.Default;
        while((piece = state.Pieces.GetNext(piece)) != PieceProperty.Default)
        {
            state.Pos.Piece = piece;

            ref var entry = ref this.tt.GetEntry(ref pos, out var hit);
            if(hit)
            {
                lower = Math.Max(lower, -entry.Upper);
                upper = Math.Max(upper, -entry.Lower);
            }
            else
                upper = SCORE_MATE;
        }

        state.Pos.Piece = prevChoice;
        return (lower, upper);
    }

    static bool SearchMateInOneMove(ref SearchState state)
    {
        var coord = -1;
        while((coord = state.Empties.GetNext(coord)) != -1)
        {
            state.Pos.Update(coord);
            var isQuarto = state.Pos.IsQuarto;
            state.Pos.Undo(state.Pos.Piece, coord);

            if(isQuarto)
                return true;
        }
        return false;
    }

    struct SearchState(Position pos, PieceList pieces, LinkedList16 empties)
    {
        public Position Pos = pos;
        public PieceList Pieces = pieces;
        public LinkedList16 Empties = empties;
    }
}