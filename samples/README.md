# Sample photos

Ready-made photos for trying the workflow. With otherwise valid details (adult aged 21+, category `B`, a national
ID that is not on the watchlist), each one leads to a known outcome:

| File | Size | Quality score | Outcome | History `details` |
|---|---:|---:|---|---|
| `approve.png` | 57 KB | 96 | **Approved** (automatically) | — |
| `manual-review.png` | 58 KB | 40 | **PendingManualReview** | Photo quality score 40 is below the review threshold of 60 |
| `fail-low-quality.png` | 57 KB | 15 | **Failed** | Photo quality score 15 is below the minimum of 30 |
| `fail-too-small.png` | 331 B | 85 | **Failed** | File size 331 bytes is below the minimum of 10240 bytes |
| `fail-unsupported-type.bmp` | 53 KB | 96 | **Failed** | File type is unsupported |

Each failing sample fails for exactly one reason; the others pass every photo rule.

## How they are made

The quality check is simulated (decision D-5): the score is derived from the SHA-256 of the file's bytes, so the
same file always gets the same score. `generate.cs` draws a plain head-and-shoulders silhouette and varies a
text comment embedded in the PNG (invisible in the picture) until the score lands well inside the wanted range:
75 or above to approve, 40–50 for review, below 20 to fail. The scores sit away from the default thresholds
(30 and 60), so small threshold changes don't change the outcomes.

To regenerate them (for example after changing the thresholds in `WorkflowRules:Photo`), from this folder:

```bash
dotnet run generate.cs -- .
```
