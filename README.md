# EtaEstimator

A ETA (Estimated Time of Arrival) predictor.
Combines filtering, statistics (quantile estimator, rolling mean/MAD, Huber), ... in order to provide accurate and smooth output values.
Probably a lil. bit overkill.

# Features
- **Multiple estimation strategies combined:**
- Exponential Moving Average (EMA)
- Welford variance/mean
- Regression (RLS trend model)
- Pace filter (drift/noise)
- P^2 quantile estimator (O(1))

# Display stabilization

- Capped drops (max drops per second)
- Grace period for rises
- Snap-to-zero near the end
- Regime-shift detection
- Detects speed changes via rolling mean + MAD
- Adapts learning rate dynamically

# API Overview

## EtaOptions
Configuration for filters, tolerances, stabilization, ...:

```
EtaOptions(
    OutlierCut: 3.0,          // Anything more than ~3 off = outlier >>> gets down-weighted
    NoiseBlend: 0.15,         // How much random noise we let into the model
    DriftFactor: 0.02,        // Speed of adapting to real trend changes
    Forget: 0.995,            // Slowly forgets old data >>> model keeps adapting even after hours
    EmaAlpha: 0.12,           // Weight for the normal EMA
    MaxDropPerSec: 1.0,       // ETA can only drop 1s per real second >>> no big jumps
    RiseGraceSec: 3.0,        // After a drop, freeze rises for 3s >>> no annoying jumps
    RiseMinJump: 2,           // Only let ETA rise if it’s at least +2s >>> ignore tiny bumps
    ColdStartSecPerUnit: 0.4, // Initial guess: each unit takes ~0.4s until real data kicks in
    WarmupSamples: 20,        // Gather first 20 samples before filters calm down
    EmaAlphaWarmup: 0.37,     // Learn faster in warmup (higher alpha), then slow down later
    MaxLagAtEnd: 1.5,         // Close the end ETA can lag max 1.5s before snapping to 0
    LagSlopeSqrt: 0.75,       // Allowed lag grows with sqrt(ETA) >>> big job can be lazier
    NearEndSnapSec: 8.0,      // If less than 8s left >>> force ETA to snap to 0 quickly
    QuantileP: 0.70,          // Use p70 pace estimate
    RegimeAlpha: 0.10,        // Smoothing factor for rolling mean/MAD >>> how fast we learn a new “mode”
    RegimeThreshold: 4.0,     // Trigger regime shift if |x - mean| > 4 * MAD >>> basically “something changed big time”
    RegimeWarmupSteps: 8      // After a regime shift, learn aggressively for 8 steps
);
```

## EtaEstimator
Main class:

```
var eta = new ETAEstimator(totalUnits: 200);

// Report progress
eta.Step(); // = 1 unit
eta.Step(5); // = 5 units

// Get
double seconds = eta.GetEtaSeconds(stable: true);
```

## EtaSnapshot
Detailed info as record:

```
var snap = eta.Snapshot();

Console.WriteLine($"ETA: {snap.RemainingSeconds:F1}s");
Console.WriteLine($"Progress: {snap.Percent:F1}%");
Console.WriteLine($"EMA pace: {snap.SecPerUnitEma}");
Console.WriteLine($"Filtered pace: {snap.SecPerUnitFiltered}");
```

# WPF Demo
The repo includes a small WPF test application to test the estimator:

- Simulated progress bar
- Shows unstable ETA (raw) vs. stable ETA (filtered)
- Visualizes filter adjustments and jumps

# Use Cases

- Long loops with fluctuating iteration runtime
- ...
