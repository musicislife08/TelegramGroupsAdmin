using NUnit.Framework;

// Run test classes (fixtures) in parallel, but tests within each class run sequentially
// Each test class creates its own isolated database, so this is safe
[assembly: Parallelizable(ParallelScope.Fixtures)]
