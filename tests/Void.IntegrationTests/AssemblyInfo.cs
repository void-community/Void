using Void.IntegrationTests.Infrastructure.Fixtures;
using Xunit;
using Xunit.v3;

#if DEBUG
[assembly: CaptureConsole]
#endif

#if GITHUB_ACTIONS
[assembly: Parallelization(MaxThreads = 1)]
#else
[assembly: Parallelization(MaxThreads = 10)]
#endif

[assembly: AssemblyFixture(typeof(PaperFixture))]
