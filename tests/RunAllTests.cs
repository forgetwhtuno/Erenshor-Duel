using System;

namespace ErenshorDuel.Tests
{
    internal static class RunAllTests
    {
        private static int Main()
        {
            string result = ErenshorDuel.DuelSelfTests.RunAll();
            Console.WriteLine(result);
            if (!result.StartsWith("PASS", StringComparison.Ordinal))
            {
                Console.WriteLine("RunAllTests: FAIL");
                return 1;
            }
            Console.WriteLine("RunAllTests: PASS");
            return 0;
        }
    }
}
