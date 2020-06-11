namespace FastqDetective.Parsing
{
    public class MarkerMatch
    {
        public readonly string Locus;
        public readonly double Score;

        public readonly string SequenceChunk;

        public MarkerMatch(string locus, double score, string sequenceChunk)
        {
            Locus = locus;
            Score = score;
            SequenceChunk = sequenceChunk;
        }
    }
}
