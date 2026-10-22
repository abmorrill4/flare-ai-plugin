using Xunit;

// Scenarios share process-wide state (Paths.Root, Team.Current, diagnostics); run them one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
