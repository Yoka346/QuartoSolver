using System;

namespace Quarto.Utils;

public static class Permutation
{
    public static bool Next<T>(Span<T> seq) where T : IComparable<T>
    {
        int i;
        for(i = seq.Length - 2; i >= 0 && seq[i].CompareTo(seq[i + 1]) >= 0; i--);

        if(i == -1)
            return false;

        int j;
        for(j = seq.Length - 1; j >= 0 && seq[j].CompareTo(seq[i]) <= 0; j--);

        (seq[i], seq[j]) = (seq[j], seq[i]);

        var subSeq = seq[(i + 1)..];
        for(var k = 0; k < subSeq.Length / 2; k++)
            (subSeq[k], subSeq[^(k + 1)]) = (subSeq[^(k + 1)], subSeq[k]);

        return true;
    }
}