# FastqDetective

FastqDetective is a Windows desktop application written in C# for searching marker sequences in FASTQ sequencing data. It evaluates candidate matches against sequence data and reports matching regions according to the application's scoring and matching settings.

## Overview

FASTQ files store sequencing reads alongside per-base quality information. FastqDetective is designed to help locate sequences of interest within this data by comparing marker sequences with read sequence content.

The application processes sequence data in chunks and supports work across multiple marker and chunk combinations. This approach allows independent search tasks to be processed concurrently while keeping the amount of sequence data handled by each task bounded.

## Algorithmic approach

### Dynamic-programming sequence scoring

FastqDetective uses a Smith–Waterman-style dynamic-programming approach for evaluating sequence similarity. The scoring behavior depends on the configured scoring rules, including match, mismatch, and gap scores.

### Rolling arrays

A dynamic-programming alignment matrix can require memory proportional to the product of the two sequence lengths if every cell is retained. Rolling arrays keep only the rows or columns needed for the next calculation. This reduces working memory for score calculation while retaining the dynamic-programming traversal.

Rolling arrays reduce memory use; by themselves, they do not reduce the number of score calculations required.

### Potential-based pruning

Potential-based pruning uses an upper bound on the best score that could still be reached from the unprocessed portion of a sequence. If that bound cannot meet a required threshold or improve on an already established score, the remaining calculations may be skipped, provided the bound is valid for the scoring model and the required result.

The correctness of pruning depends on the scoring rules and on applying the bound conservatively. It should be validated against an unpruned reference calculation before relying on it for production results.

### Concurrency

Independent marker/chunk search tasks can be scheduled concurrently. Rolling arrays and potential-based pruning address different costs: rolling arrays target memory usage, while pruning targets unnecessary computation. Concurrent scheduling can complement both, although actual performance depends on workload and available hardware.

## Requirements

The project has ooooold origins, but target framework was refreshed. It's a C# Windows Presentation Foundation (WPF) application targeting .NET 10.0. A compatible Windows development environment and .NET SDK are needed to build it.

## Input data

Use FASTQ files that conform to the expected four-line FASTQ record structure. Confirm that input files are complete and that the marker sequences and scoring settings are appropriate for the analysis being performed.

