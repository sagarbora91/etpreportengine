// Fixtures intentionally retain protective, process-wide ClearAllPools calls.
// Do not let one collection dispose pools while another collection is using SQL.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
