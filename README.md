# FastqDetective

FastqDetective is a C# application for finding matches of one or more marker sequences in FASTQ-formatted sequencing data. It reads sequence records, divides sequence data into chunks, evaluates candidate marker matches, and collects results for reporting.

This README describes the application's processing model, its alignment-scoring approach, its concurrency model, and the changes made in the revised source. Where the implementation differs from standard Smith–Waterman, that distinction is noted explicitly.

## Table of contents

- [Overview](#overview)
- [How the application processes FASTQ data](#how-the-application-processes-fastq-data)
- [Sequence chunking](#sequence-chunking)
- [Marker matching and alignment scoring](#marker-matching-and-alignment-scoring)
- [Smith–Waterman local alignment](#smithwaterman-local-alignment)
- [Potential-based pruning](#potential-based-pruning)
- [Concurrency and task scheduling](#concurrency-and-task-scheduling)
- [Memory usage](#memory-usage)
- [Source code organization](#source-code-organization)
- [Important implementation details](#important-implementation-details)
- [Current limitations and TODOs](#current-limitations-and-todos)
- [Testing and validation](#testing-and-validation)
- [License](#license)

## Overview

FASTQ is a text-based format used to store nucleotide sequences and their associated quality information. FastqDetective processes sequence data and searches for specified marker sequences.

The main processing stages are:

1. Read FASTQ records.
2. Accumulate sequence and quality data.
3. Divide the sequence into overlapping chunks.
4. Schedule marker-versus-chunk evaluations.
5. Calculate alignment scores and identify candidate matching regions.
6. Apply the configured threshold and potential-based pruning.
7. Collect and report the resulting matches.

The application uses multiple levels of processing: FASTQ parsing and chunk preparation, concurrent evaluation of marker/chunk pairs, and a dynamic-programming scoring routine inside each evaluation.

The source contains a custom regional alignment implementation inspired by Smith–Waterman. It should not be assumed to be identical to a canonical Smith–Waterman implementation in every detail.

## How the application processes FASTQ data

### Reading records

The parser reads the input file line by line and uses a four-step state machine to interpret FASTQ records:

1. Header line.
2. Sequence line.
3. Separator line.
4. Quality or certainty line.

Sequence and quality information are accumulated in separate buffers. The parser uses these buffers to prepare sequence chunks for matching.

The original parser uses a `ParseStep` state to keep track of which line of a FASTQ record it is processing. This means that the expected input format is the conventional four-line FASTQ record structure.

**Input-format consideration:** FASTQ files can also contain sequences wrapped across multiple lines. Such files should be tested explicitly against the parser rather than assumed to be supported in every valid FASTQ variant.

### Sequence and quality data

The parser maintains separate sequence and quality/certainty buffers. It converts chunk contents to ASCII byte arrays before scheduling matching work.

The current scoring implementation accepts the quality/certainty byte array as an argument, but does not use those values to adjust the alignment score. Matching is therefore based on sequence characters, not on a quality-weighted scoring model.

## Sequence chunking

Large sequence inputs are processed in chunks rather than as one enormous sequence.

The chunk size is controlled by `chunkSize`. After processing a chunk, the parser advances by a sliding step:

```text
slidingStep = chunkSize - slidingStepRemainder
```

When the sliding step is smaller than the chunk size, consecutive chunks overlap.

For example, if the chunk size is 1,000 bases and the sliding step is 800 bases, the chunks cover positions 0–999, 800–1,799, 1,600–2,599, and so on.

Overlap helps ensure that a marker spanning a chunk boundary can still appear completely inside at least one chunk, provided the overlap is large enough for the marker and the chunking rules are configured appropriately.

The parser tracks the starting sequence offset for each chunk so that a match found within a chunk can be associated with a position in the original sequence.

Chunking requires careful validation. In particular, tests should cover markers near chunk boundaries, the final incomplete chunk, and the relationship between reported positions and the original input coordinates.

## Marker matching and alignment scoring

Each configured marker is evaluated against each prepared sequence chunk.

The parser schedules an evaluation for each marker/chunk pair. The evaluation calls the regional alignment routine, which calculates scores for possible alignments between the marker and the chunk.

The implementation uses scoring values that include:

- Match score: `+2`
- Mismatch score: `-1`
- Gap-opening penalty: `2`
- Gap-extension penalty: `1`

The revised implementation expresses the gap penalties as negative score adjustments. These parameters should be considered part of the current scoring configuration, not universal defaults for sequence alignment.

The scorer tracks the best score and associated positions. If the best score meets the configured threshold, the evaluation produces a match result.

The threshold is used to determine whether a candidate score is high enough to be reported. Its units and normalization must be interpreted consistently with the application settings and result format.

## Smith–Waterman local alignment

### The zero-reset recurrence

The revised scoring routine includes the zero-reset term used by standard Smith–Waterman local alignment:

\[
H[i,j] = \max(0,\ D,\ U,\ L)
\]

where the candidate scores are derived from the diagonal, upper and left predecessor cells.

In the conventional linear-gap formulation, these terms are:

\[
\begin{aligned}
D &= H[i-1,j-1] + s(a_i,b_j)\\
U &= H[i-1,j] - g\\
L &= H[i,j-1] - g
\end{aligned}
\]

Here, \(s(a_i,b_j)\) is the match or mismatch score, and \(g\) is a gap penalty.

The revised implementation uses separate gap states to represent gap-opening and gap-extension penalties. This is an affine-gap variant rather than the simple linear-gap formulation shown above.

The zero-reset permits a new local alignment to begin when extending the previous candidate would produce a negative score. It is a defining feature of local alignment.

### Relationship to the original implementation

The original `RegionalSmithWaterman` routine used diagonal, up and left scoring terms but did not include the zero-reset term in the cell recurrence.

The revised version adds this term to make the scoring behavior more consistent with Smith–Waterman local alignment.

This change can alter scores and reported matches. It is not simply a performance optimization.

The recurrence alone does not establish full equivalence to a trusted Smith–Waterman implementation. Initialization, gap-state transitions, origin tracking, end-position conventions, tie handling and threshold semantics must also be validated.

### Rolling arrays

The scoring routine does not retain a complete score matrix. It uses rolling arrays to retain the score state needed for the next row or column of calculations.

This reduces memory usage compared with storing every dynamic-programming cell. It also means that the current implementation is not structured to calculate arbitrary matrix diagonals independently without changing its storage and scheduling strategy.

## Potential-based pruning

Potential-based pruning is an optimization intended to stop calculations that cannot produce a useful result.

The original source contains custom logic that estimates remaining score potential and can terminate an evaluation early. The revised implementation retains a conservative form of pruning, adapted to the zero-reset recurrence.

### Why pruning requires an upper bound

A pruning rule must not discard a candidate that could still produce a qualifying or better alignment.

With Smith–Waterman local alignment, a later row can start a completely new alignment at zero. Consequently, a bound based only on the current row's score or on the continuation of the current candidate is not necessarily safe.

The revised implementation uses a conservative upper bound for any alignment entirely within the unprocessed sequence suffix:

\[
P_{\mathrm{remaining}} =
s_{\max}\min(m,r)
\]

where:

- \(s_{\max}\) is the maximum score per matched character.
- \(m\) is the marker length.
- \(r\) is the number of sequence characters remaining.

With the current match score of `+2`, this becomes:

```text
remainingPotential = 2 * min(markerLength, remainingSequenceLength)
```

This bound assumes that every paired character in a possible remaining alignment earns the maximum match score. Mismatches and gaps cannot increase the score beyond this optimistic bound.

### When the revised implementation stops

The implementation uses the bound in two situations:

1. **Threshold pruning:** If the best score already found and the maximum possible score in the remaining suffix are both below the required threshold, no qualifying result can be returned.
2. **Best-score pruning:** If the maximum possible score in the remaining suffix is strictly less than the best score already found, later rows cannot improve the best score.

The strict comparison in the second condition preserves the current first-maximum tie behavior.

### What this pruning does not guarantee

This is a conservative bound, not a guarantee of a large speedup. It may prune relatively little when a long sequence remains or when the marker is short.

It also does not restore the original pruning heuristic exactly. The original heuristic and its state propagation require separate validation under the revised recurrence.

Before production use, the pruned scorer should be compared with an otherwise identical unpruned scorer on known-answer cases. The two should return equivalent results under the intended score, threshold and tie-breaking semantics.

## Concurrency and task scheduling

### Parallelism across marker/chunk pairs

The parser schedules separate calculations for marker/chunk pairs using `Task.Run`.

Each task receives the data needed for its own evaluation, including the sequence chunk, threshold and starting sequence offset. This avoids depending on mutable parser values that may change before a queued task starts executing.

A `SemaphoreSlim` limits the number of calculations that can be active at once. This provides a more direct concurrency limit than repeatedly checking the number of active tasks and sleeping.

Completed tasks are collected, their exceptions are observed, and successful match results are added to the corresponding result collection.

### Parallelism inside an alignment matrix

The current scoring routine traverses each dynamic-programming matrix sequentially. Its rolling-array design is optimized for sequential evaluation.

A separate potential optimization is **anti-diagonal wavefront parallelization**.

In a conventional dynamic-programming recurrence, a cell depends on its upper, left and upper-left neighbors. Cells along the same anti-diagonal can be computed concurrently once the preceding anti-diagonal is complete.

This approach is not implemented in the current source. It would require a different matrix-storage and scheduling strategy, plus synchronization between wavefronts. The effect of such a change on rolling-array memory savings, pruning and runtime would need to be measured.

The two levels of parallelism are conceptually distinct:

- **Across marker/chunk pairs:** already used by the application.
- **Within one alignment matrix:** a possible future wavefront optimization.

## Memory usage

The application accumulates sequence data in buffers and processes it in chunks. The scoring routine uses rolling arrays rather than storing the full dynamic-programming matrix.

For a marker of length \(m\), the scoring arrays scale with the marker length rather than with the product of marker length and chunk length.

The total memory footprint also depends on:

- Chunk size and overlap.
- Number of markers.
- Number of concurrent evaluations.
- FASTQ buffer sizes.
- The number of results retained during processing.

Increasing concurrency may improve throughput, but it also increases the number of simultaneous chunk evaluations and their associated temporary arrays. The best concurrency setting therefore depends on input size, marker count, CPU resources and available memory.

## Source code organization

The following files are central to the application's processing flow.

| File or class | Responsibility |
|---|---|
| `Parsing/AnalysisParser.cs` | Reads and chunks FASTQ data, schedules marker/chunk tasks, calculates alignment scores and collects matches. |
| `Parsing/ParsingContext.cs` | Holds shared parser state, sequence buffers, configuration and task counters. |
| `Parsing/ParsingResult.cs` | Holds per-marker information, active tasks and collected matches. |
| `Parsing/AnalysisParser.cs` — `RegionalSmithWaterman` | Performs the regional dynamic-programming scoring calculation. |
| `AnalysisWindow.xaml.cs` | Connects the user interface to analysis settings such as chunk size, threshold and concurrency. |
| `ResultsWriter.cs` | Handles writing analysis results and associated metadata. |

Exact responsibilities and signatures should be checked against the version of the source being built, as this README also documents changes made in the revised implementation.

## Important implementation details

### Mutable state and task safety

Values that can change while the parser is running should not be read implicitly by worker tasks. Inputs such as sequence offsets and thresholds should be captured before scheduling.

The marker byte array should also be treated as immutable while evaluations are active. If the application allows marker data to be modified in place, a copy should be made when the task is scheduled.

### Task failures

Task exceptions should be observed rather than silently interpreted as an absent match. A failed calculation is not equivalent to a valid calculation that found no qualifying alignment.

### Skipped cells

The legacy `diagSkip` optimization is disabled in the revised implementation. In the original code, the skipped-cell path could continue without explicitly assigning all score and origin entries that might later be read from reused arrays.

Reintroducing cell skipping requires proof that the skipped cells cannot affect a valid result, along with tests that verify all required array values are initialized or assigned.

### Quality information

Quality/certainty bytes are passed through the evaluation interface, but the scoring routine does not currently use them. Quality-aware scoring would be a separate feature and would need an explicitly defined scoring model.

## Current limitations and TODOs

The following work remains before the revised implementation should be treated as a verified release:

- [ ] Compile the revised project against its intended .NET target.
- [ ] Compare scores and positions with a trusted Smith–Waterman reference implementation.
- [ ] Test exact matches, mismatches, gaps, negative prefixes and competing local matches.
- [ ] Test threshold boundaries and confirm the threshold's units and normalization.
- [ ] Compare pruned and unpruned results to verify that pruning does not change valid outputs.
- [ ] Test tie handling and origin/end-position conventions.
- [ ] Test matches that start or end near chunk boundaries.
- [ ] Verify behavior with short final chunks and sequence/quality length mismatches.
- [ ] Test concurrency with varying worker limits and repeated runs.
- [ ] Verify that marker data is not mutated while tasks are running.
- [ ] Test cancellation and ensure already-started tasks are drained safely.
- [ ] Reassess the legacy `diagSkip` optimization before considering re-enabling it.
- [ ] Benchmark task-level concurrency before implementing intra-matrix wavefront parallelism.

## Testing and validation

Correctness should be established before performance is optimized.

A useful test strategy is to create short marker and sequence pairs whose expected scores and positions can be calculated by hand. Include examples where a high-scoring match follows a negative-scoring prefix, because these cases specifically exercise the Smith–Waterman zero reset.

Next, run the same test cases with pruning enabled and disabled. The reported result should remain consistent with the chosen scoring and tie-breaking rules.

Only after these checks pass should performance comparisons be used to assess chunk sizes, concurrency limits, pruning effectiveness or a future wavefront implementation.

No performance improvement should be assumed without measurement on representative FASTQ inputs.

## License

The original FastqDetective repository is distributed under the GNU General Public License version 3.0 (GPL-3.0). Consult the repository's `LICENSE` file for the full license terms.
